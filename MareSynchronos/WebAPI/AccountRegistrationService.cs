using MareSynchronos.API.Dto.Account;
using MareSynchronos.API.Routes;
using MareSynchronos.Services;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.SignalR;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;

namespace MareSynchronos.WebAPI;

public sealed class AccountRegistrationService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AccountRegistrationService> _logger;
    private readonly ServerConfigurationManager _serverManager;
    private readonly RemoteConfigurationService _remoteConfig;

    private string GenerateSecretKey()
    {
        return Convert.ToHexString(SHA256.HashData(RandomNumberGenerator.GetBytes(64)));
    }

    public AccountRegistrationService(ILogger<AccountRegistrationService> logger, ServerConfigurationManager serverManager, RemoteConfigurationService remoteConfig)
    {
        _logger = logger;
        _serverManager = serverManager;
        _remoteConfig = remoteConfig;
        _httpClient = new(
            new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            }
        );
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", ver!.Major + "." + ver!.Minor + "." + ver!.Build));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<RegisterReplyDto> RegisterAccount(CancellationToken token)
    {
        var authApiUrl = _serverManager.CurrentApiUrl;

        // Override the API URL used for auth from remote config, if one is available
        if (authApiUrl.Equals(ApiController.UmbraServiceUri, StringComparison.Ordinal))
        {
            var config = await _remoteConfig.GetConfigAsync<HubConnectionConfig>("mainServer").ConfigureAwait(false) ?? new();
            if (!string.IsNullOrEmpty(config.ApiUrl))
                authApiUrl = config.ApiUrl;
            else
                authApiUrl = ApiController.UmbraServiceApiUri;
        }

        var secretKey = GenerateSecretKey();
        var hashedSecretKey = secretKey.GetHash256();

        Uri postUri = MareAuth.AuthRegisterV2FullPath(new Uri(authApiUrl
            .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
            .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));

        var result = await _httpClient.PostAsync(postUri, new FormUrlEncodedContent([
            new("hashedSecretKey", hashedSecretKey)
        ]), token).ConfigureAwait(false);
        result.EnsureSuccessStatusCode();

        var response = await result.Content.ReadFromJsonAsync<RegisterReplyV2Dto>(token).ConfigureAwait(false) ?? new();

        return new RegisterReplyDto()
        {
            Success = response.Success,
            ErrorMessage = response.ErrorMessage,
            UID = response.UID,
            SecretKey = secretKey
        };
    }
}