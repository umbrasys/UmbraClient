using Microsoft.Extensions.Logging;
using UmbraSync.Services.Mediator;
using UmbraSync.API.Dto.CharaData;
using Microsoft.Extensions.Hosting;

namespace UmbraSync.Services;

public class HousingMonitorService : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<HousingMonitorService> _logger;
    private readonly MareMediator _mediator;
    private readonly DalamudUtilService _dalamudUtil;
    private LocationInfo _lastLocation = new();
    private CancellationTokenSource? _loopCts;

    public HousingMonitorService(ILogger<HousingMonitorService> logger, MareMediator mediator, DalamudUtilService dalamudUtil)
    {
        _logger = logger;
        _mediator = mediator;
        _dalamudUtil = dalamudUtil;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting HousingMonitorService");
        _loopCts = new CancellationTokenSource();
        _ = Task.Run(() => Loop(_loopCts.Token), _loopCts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping HousingMonitorService");
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _mediator.UnsubscribeAll(this);
        return Task.CompletedTask;
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var currentLocation = await _dalamudUtil.GetMapDataAsync().ConfigureAwait(false);
                if (currentLocation.ServerId == 0)
                {
                    await Task.Delay(5000, ct).ConfigureAwait(false);
                    continue;
                }

                // Toujours envoyer la position si on est dans un territoire de housing (TerritoryId change ou position change assez)
                var player = await _dalamudUtil.GetPlayerCharacterAsync().ConfigureAwait(false);
                if (player != null)
                {
                    _mediator.Publish(new HousingPositionUpdateMessage(currentLocation.ServerId, currentLocation.TerritoryId, player.Position));
                }

                bool hasChanged = currentLocation.ServerId != _lastLocation.ServerId ||
                                 currentLocation.TerritoryId != _lastLocation.TerritoryId ||
                                 currentLocation.WardId != _lastLocation.WardId ||
                                 currentLocation.HouseId != _lastLocation.HouseId;

                if (hasChanged)
                {
                    _logger.LogDebug("Location changed from {lastServer}:{lastTerritory}:{lastWard}:{lastHouse} to {currentServer}:{currentTerritory}:{currentWard}:{currentHouse}", 
                        _lastLocation.ServerId, _lastLocation.TerritoryId, _lastLocation.WardId, _lastLocation.HouseId,
                        currentLocation.ServerId, currentLocation.TerritoryId, currentLocation.WardId, currentLocation.HouseId);
                    
                    bool wasInHousing = _lastLocation.HouseId != 0;
                    bool isInHousing = currentLocation.HouseId != 0;

                    if (isInHousing)
                    {
                        _mediator.Publish(new HousingPlotEnteredMessage(currentLocation));
                    }
                    else if (wasInHousing)
                    {
                        _mediator.Publish(new HousingPlotLeftMessage());
                    }

                    _lastLocation = currentLocation;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HousingMonitorService Loop");
            }

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }

    public MareMediator Mediator => _mediator;
}
