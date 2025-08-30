using Chaos.NaCl;
using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MareSynchronos.Services;

public sealed class RemoteConfigurationService
{
    private readonly static Dictionary<string, string> ConfigPublicKeys = new(StringComparer.Ordinal)
    {
        { "UMBR4KEY", "+MwCXedODmU+yD7vtdI+Ho2iLx+PV3U0H2XRLP/gReA=" }
    };

    private readonly static string[] ConfigSources = [
        "https://umbra-sync.net/config/umbra.json"
    ];

    private readonly ILogger<RemoteConfigurationService> _logger;
    private readonly RemoteConfigCacheService _configService;
    private readonly Task _initTask;

    public RemoteConfigurationService(ILogger<RemoteConfigurationService> logger, RemoteConfigCacheService configService)
    {
        _logger = logger;
        _configService = configService;
        _initTask = Task.Run(DownloadConfig);
    }

    public async Task<JsonObject> GetConfigAsync(string sectionName)
    {
        await _initTask.ConfigureAwait(false);
        if (!_configService.Current.Configuration.TryGetPropertyValue(sectionName, out var section))
            section = null;
        return (section as JsonObject) ?? new();
    }

    public async Task<T?> GetConfigAsync<T>(string sectionName)
    {
        try
        {
            var json = await GetConfigAsync(sectionName).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in remote config: {sectionName}", sectionName);
            return default;
        }
    }

    private async Task DownloadConfig()
    {
        string? jsonResponse = null;

        foreach (var remoteUrl in ConfigSources)
        {
            try
            {
                _logger.LogDebug("Fetching {url}", remoteUrl);

                using var httpClient = new HttpClient(
                    new HttpClientHandler
                    {
                        AllowAutoRedirect = true,
                        MaxAutomaticRedirections = 5
                    }
                );

                httpClient.Timeout = TimeSpan.FromSeconds(6);

                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", ver!.Major + "." + ver!.Minor + "." + ver!.Build));

                var request = new HttpRequestMessage(HttpMethod.Get, remoteUrl);

                if (remoteUrl.Equals(_configService.Current.Origin, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(_configService.Current.ETag))
                        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(_configService.Current.ETag));

                    if (_configService.Current.LastModified != null)
                        request.Headers.IfModifiedSince = _configService.Current.LastModified;
                }

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    _logger.LogDebug("Using cached remote configuration from {url}", remoteUrl);
                    return;
                }

                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType;

                if (contentType == null || !contentType.Equals("application/json", StringComparison.Ordinal))
                {
                    _logger.LogWarning("HTTP request for remote config failed: wrong MIME type");
                    continue;
                }

                _logger.LogInformation("Downloaded new configuration from {url}", remoteUrl);

                _configService.Current.Origin = remoteUrl;
                _configService.Current.ETag = response.Headers.ETag?.ToString() ?? string.Empty;

                try
                {
                    if (response.Content.Headers.Contains("Last-Modified"))
                    {
                        var lastModified = response.Content.Headers.GetValues("Last-Modified").First();
                        _configService.Current.LastModified = DateTimeOffset.Parse(lastModified, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    _configService.Current.LastModified = null;
                }

                jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HTTP request for remote config failed");

                if (remoteUrl.Equals(_configService.Current.Origin, StringComparison.Ordinal))
                {
                    _configService.Current.ETag = string.Empty;
                    _configService.Current.LastModified = null;
                    _configService.Save();
                }
            }
        }

        if (jsonResponse == null)
        {
            _logger.LogWarning("Could not download remote config");
            return;
        }

        try
        {
            var jsonDoc = JsonNode.Parse(jsonResponse) as JsonObject;

            if (jsonDoc == null)
            {
                _logger.LogWarning("Downloaded remote config is not a JSON object");
                return;
            }

            LoadConfig(jsonDoc);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in remote config response");
        }
    }

    private static bool VerifySignature(string message, ulong ts, string signature, string pubKey)
    {
        byte[] msg = [.. BitConverter.GetBytes(ts), .. Encoding.UTF8.GetBytes(message)];
        byte[] sig = Convert.FromBase64String(signature);
        byte[] pub = Convert.FromBase64String(pubKey);
        return Ed25519.Verify(sig, msg, pub);
    }

    private void LoadConfig(JsonObject jsonDoc)
    {
        var ts = jsonDoc["ts"]!.GetValue<ulong>();

        if (ts <= _configService.Current.Timestamp)
        {
            _logger.LogDebug("Remote configuration is not newer than cached config");
            return;
        }

        var signatures = jsonDoc["sig"]!.AsObject();
        var configString = jsonDoc["config"]!.GetValue<string>();
        bool verified = signatures.Any(sig =>
            ConfigPublicKeys.TryGetValue(sig.Key, out var pubKey) &&
                VerifySignature(configString, ts, sig.Value!.GetValue<string>(), pubKey));

        if (!verified)
        {
            _logger.LogWarning("Could not verify signature for downloaded remote config");
            return;
        }

        _configService.Current.Configuration = JsonNode.Parse(configString)!.AsObject();
        _configService.Current.Timestamp = ts;
        _configService.Save();
    }
}
