using Microsoft.Extensions.Logging;
using MareSynchronos.WebAPI.AutoDetect;
using MareSynchronos.MareConfiguration;

namespace MareSynchronos.Services.AutoDetect;

public class AutoDetectRequestService
{
    private readonly ILogger<AutoDetectRequestService> _logger;
    private readonly DiscoveryConfigProvider _configProvider;
    private readonly DiscoveryApiClient _client;
    private readonly MareConfigService _configService;

    public AutoDetectRequestService(ILogger<AutoDetectRequestService> logger, DiscoveryConfigProvider configProvider, DiscoveryApiClient client, MareConfigService configService)
    {
        _logger = logger;
        _configProvider = configProvider;
        _client = client;
        _configService = configService;
    }

    public async Task<bool> SendRequestAsync(string token, CancellationToken ct = default)
    {
        if (!_configService.Current.AllowAutoDetectPairRequests)
        {
            _logger.LogDebug("Nearby request blocked: AllowAutoDetectPairRequests is disabled");
            return false;
        }
        var endpoint = _configProvider.RequestEndpoint;
        if (string.IsNullOrEmpty(endpoint))
        {
            _logger.LogDebug("No request endpoint configured");
            return false;
        }
        return await _client.SendRequestAsync(endpoint!, token, ct).ConfigureAwait(false);
    }
}
