using Microsoft.Extensions.Logging;
using MareSynchronos.WebAPI.AutoDetect;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using NotificationType = MareSynchronos.MareConfiguration.Models.NotificationType;

namespace MareSynchronos.Services.AutoDetect;

public class AutoDetectRequestService
{
    private readonly ILogger<AutoDetectRequestService> _logger;
    private readonly DiscoveryConfigProvider _configProvider;
    private readonly DiscoveryApiClient _client;
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamud;
    private readonly MareMediator _mediator;

    public AutoDetectRequestService(ILogger<AutoDetectRequestService> logger, DiscoveryConfigProvider configProvider, DiscoveryApiClient client, MareConfigService configService, MareMediator mediator, DalamudUtilService dalamudUtilService)
    {
        _logger = logger;
        _configProvider = configProvider;
        _client = client;
        _configService = configService;
        _mediator = mediator;
        _dalamud = dalamudUtilService;
    }

    public async Task<bool> SendRequestAsync(string token, CancellationToken ct = default)
    {
        if (!_configService.Current.AllowAutoDetectPairRequests)
        {
            _logger.LogDebug("Nearby request blocked: AllowAutoDetectPairRequests is disabled");
            _mediator.Publish(new NotificationMessage("Nearby request blocked", "Enable 'Allow pair requests' in Settings to send requests.", NotificationType.Info));
            return false;
        }
        var endpoint = _configProvider.RequestEndpoint;
        if (string.IsNullOrEmpty(endpoint))
        {
            _logger.LogDebug("No request endpoint configured");
            _mediator.Publish(new NotificationMessage("Nearby request failed", "Server does not expose request endpoint.", NotificationType.Error));
            return false;
        }
        string? displayName = null;
        try
        {
            var me = await _dalamud.RunOnFrameworkThread(() => _dalamud.GetPlayerCharacter()).ConfigureAwait(false);
            displayName = me?.Name.TextValue;
        }
        catch { }

        _logger.LogInformation("Nearby: sending pair request via {endpoint}", endpoint);
        var ok = await _client.SendRequestAsync(endpoint!, token, displayName, ct).ConfigureAwait(false);
        if (ok)
        {
            _mediator.Publish(new NotificationMessage("Nearby request sent", "The other user will receive a request notification.", NotificationType.Info));
        }
        else
        {
            _mediator.Publish(new NotificationMessage("Nearby request failed", "The server rejected the request. Try again soon.", NotificationType.Warning));
        }
        return ok;
    }

    public async Task<bool> SendAcceptNotifyAsync(string targetUid, CancellationToken ct = default)
    {
        var endpoint = _configProvider.AcceptEndpoint;
        if (string.IsNullOrEmpty(endpoint))
        {
            _logger.LogDebug("No accept endpoint configured");
            return false;
        }
        string? displayName = null;
        try
        {
            var me = await _dalamud.RunOnFrameworkThread(() => _dalamud.GetPlayerCharacter()).ConfigureAwait(false);
            displayName = me?.Name.TextValue;
        }
        catch { }
        _logger.LogInformation("Nearby: sending accept notify via {endpoint}", endpoint);
        return await _client.SendAcceptAsync(endpoint!, targetUid, displayName, ct).ConfigureAwait(false);
    }
}
