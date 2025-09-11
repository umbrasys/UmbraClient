using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MareSynchronos.WebAPI.SignalR;

namespace MareSynchronos.WebAPI.AutoDetect;

public class DiscoveryApiClient
{
    private readonly ILogger<DiscoveryApiClient> _logger;
    private readonly TokenProvider _tokenProvider;
    private readonly HttpClient _httpClient = new();
    private static readonly JsonSerializerOptions JsonOpt = new() { PropertyNameCaseInsensitive = true };

    public DiscoveryApiClient(ILogger<DiscoveryApiClient> logger, TokenProvider tokenProvider)
    {
        _logger = logger;
        _tokenProvider = tokenProvider;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<List<ServerMatch>> QueryAsync(string endpoint, IEnumerable<string> hashes, CancellationToken ct)
    {
        try
        {
            var token = await _tokenProvider.GetOrUpdateToken(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token)) return [];
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var body = JsonSerializer.Serialize(new { hashes = hashes.Distinct(StringComparer.Ordinal).ToArray() });
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<List<ServerMatch>>(json, JsonOpt) ?? [];
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discovery query failed");
            return [];
        }
    }

    public async Task<bool> SendRequestAsync(string endpoint, string token, string? displayName, CancellationToken ct)
    {
        try
        {
            var jwt = await _tokenProvider.GetOrUpdateToken(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(jwt)) return false;
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            var body = JsonSerializer.Serialize(new { token, displayName });
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string txt = string.Empty;
                try { txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { }
                _logger.LogWarning("Discovery request failed: {code} {reason} {body}", (int)resp.StatusCode, resp.ReasonPhrase, txt);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discovery send request failed");
            return false;
        }
    }

    public async Task<bool> PublishAsync(string endpoint, IEnumerable<string> hashes, string? displayName, CancellationToken ct)
    {
        try
        {
            var jwt = await _tokenProvider.GetOrUpdateToken(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(jwt)) return false;
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            var bodyObj = new { hashes = hashes.Distinct(StringComparer.Ordinal).ToArray(), displayName };
            var body = JsonSerializer.Serialize(bodyObj);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discovery publish failed");
            return false;
        }
    }
}

public sealed class ServerMatch
{
    public string Hash { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
