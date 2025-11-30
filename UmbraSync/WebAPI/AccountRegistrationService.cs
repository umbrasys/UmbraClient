using UmbraSync.API.Dto.Account;
using UmbraSync.API.Routes;
using UmbraSync.Services;
using UmbraSync.Services.ServerConfiguration;
using UmbraSync.Utils;
using UmbraSync.WebAPI.SignalR;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;

namespace UmbraSync.WebAPI;

public sealed class AccountRegistrationService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ServerConfigurationManager _serverManager;

    private static string GenerateSecretKey()
    {
        return Convert.ToHexString(SHA256.HashData(RandomNumberGenerator.GetBytes(64)));
    }

    public AccountRegistrationService(ServerConfigurationManager serverManager)
    {
        _serverManager = serverManager;
        _httpClient = new(
            new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            }
        );
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = ver is null ? "unknown" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", versionString));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UmbraSync", versionString));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<RegisterReplyDto> RegisterAccount(CancellationToken token)
    {
        var secretKey = GenerateSecretKey();
        var hashedSecretKey = secretKey.GetHash256();

        Uri postUri = MareAuth.AuthRegisterV2FullPath(new Uri(_serverManager.CurrentApiUrl
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
