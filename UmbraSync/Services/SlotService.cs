using Microsoft.Extensions.Logging;
using UmbraSync.Services.Mediator;
using UmbraSync.WebAPI.SignalR;
using UmbraSync.API.Dto.Slot;
using UmbraSync.API.Dto.CharaData;
using UmbraSync.API.Dto.Group;
using UmbraSync.Services.AutoDetect;
using UmbraSync.API.Data;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Localization;

using System.Numerics;

namespace UmbraSync.Services;

public class SlotService : MediatorSubscriberBase, IDisposable
{
    private readonly ApiController _apiController;
    private readonly SyncshellDiscoveryService _syncshellDiscoveryService;
    private readonly MareConfigService _configService;
    private readonly PairManager _pairManager;
    private readonly ChatService _chatService;
    private SlotSyncshellDto? _currentSlotSyncshell;
    private bool _joinedViaSlot = false;
    private CancellationTokenSource? _leaveTimerCts;
    private readonly HashSet<Guid> _declinedSlots = [];

    public SlotService(ILogger<SlotService> logger, MareMediator mediator, ApiController apiController, 
        SyncshellDiscoveryService syncshellDiscoveryService, MareConfigService configService, PairManager pairManager,
        ChatService chatService) 
        : base(logger, mediator)
    {
        _apiController = apiController;
        _syncshellDiscoveryService = syncshellDiscoveryService;
        _configService = configService;
        _pairManager = pairManager;
        _chatService = chatService;

        Mediator.Subscribe<HousingPlotEnteredMessage>(this, (msg) => _ = OnHousingPlotEntered(msg.LocationInfo));
        Mediator.Subscribe<HousingPlotLeftMessage>(this, (msg) => OnHousingPlotLeft());
        Mediator.Subscribe<HousingPositionUpdateMessage>(this, (msg) => OnHousingPositionUpdate(msg.ServerId, msg.TerritoryId, msg.Position));
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => ClearState());
    }

    private SlotInfoResponseDto? _detectedSlotByDistance = null;
    private SlotInfoResponseDto? _lastNotifiedSlot = null;
    private Vector3 _lastQueryPosition = Vector3.Zero;

    private static bool IsResidentialArea(uint territoryId)
    {
        return territoryId switch
        {
            339 or 340 or 341 or 641 or 979 => true,
            _ => false
        };
    }

    private async void OnHousingPositionUpdate(uint serverId, uint territoryId, Vector3 position)
    {
        if (!_configService.Current.EnableSlotNotifications) return;

        // Uniquement dans les quartiers résidentiels
        if (!IsResidentialArea(territoryId)) return;

        // On ne fait rien si on a déjà rejoint une Syncshell via Slot
        if (_joinedViaSlot) return;

        // On évite de spammer les requêtes au serveur
        if (Vector3.Distance(position, _lastQueryPosition) < 2.0f) return;
        _lastQueryPosition = position;

        var slotInfo = await _apiController.SlotGetNearby(serverId, territoryId, position.X, position.Y, position.Z).ConfigureAwait(false);
        
        // Si on détecte un slot à proximité et qu'on n'en avait pas ou que c'est un différent
        if (slotInfo != null && _detectedSlotByDistance == null)
        {
            if (_declinedSlots.Contains(slotInfo.SlotId)) return;

            _detectedSlotByDistance = slotInfo;
            if (slotInfo.AssociatedSyncshell != null)
            {
                _currentSlotSyncshell = slotInfo.AssociatedSyncshell;
                var isMember = _pairManager.Groups.Any(g => string.Equals(g.Key.GID, slotInfo.AssociatedSyncshell.Gid, StringComparison.Ordinal));
                if (isMember)
                {
                    if (_lastNotifiedSlot?.SlotId != slotInfo.SlotId)
                    {
                        _lastNotifiedSlot = slotInfo;
                        Mediator.Publish(new NotificationMessage(Loc.Get("SlotPopup.Title"), string.Format(Loc.Get("Slot.Toast.Welcome"), slotInfo.SlotName), MareConfiguration.Models.NotificationType.Info));
                    }
                }
                else
                {
                    _lastNotifiedSlot = slotInfo;
                    Mediator.Publish(new OpenSlotPromptMessage(slotInfo));
                }
            }
        }
        else if (slotInfo == null)
        {
            _detectedSlotByDistance = null;
        }
    }

    private void ClearState()
    {
        _leaveTimerCts?.Cancel();
        _leaveTimerCts?.Dispose();
        _leaveTimerCts = null;
        _currentSlotSyncshell = null;
        _joinedViaSlot = false;
    }

    public void MarkJoinedViaSlot(SlotSyncshellDto syncshell)
    {
        _currentSlotSyncshell = syncshell;
        _joinedViaSlot = true;
        Logger.LogInformation("Syncshell {name} marked as joined via Slot", syncshell.Name);
    }

    private async Task OnHousingPlotEntered(LocationInfo location)
    {
        if (!_configService.Current.EnableSlotNotifications) return;
        if (!IsResidentialArea(location.TerritoryId)) return;

        Logger.LogInformation("Entered housing plot: {location}", location);
        _leaveTimerCts?.Cancel();
        _leaveTimerCts?.Dispose();
        _leaveTimerCts = null;

        var slotLocation = new SlotLocationDto
        {
            ServerId = location.ServerId,
            TerritoryId = location.TerritoryId,
            WardId = location.WardId,
            PlotId = location.HouseId
        };

        var slotInfo = await _apiController.SlotGetInfo(slotLocation).ConfigureAwait(false);
        if (slotInfo != null)
        {
            if (_declinedSlots.Contains(slotInfo.SlotId)) return;

            Logger.LogInformation("Slot detected: {name}", slotInfo.SlotName);
            if (slotInfo.AssociatedSyncshell != null)
            {
                _currentSlotSyncshell = slotInfo.AssociatedSyncshell;
                var isMember = _pairManager.Groups.Any(g => string.Equals(g.Key.GID, slotInfo.AssociatedSyncshell.Gid, StringComparison.Ordinal));
                if (isMember)
                {
                    if (_lastNotifiedSlot?.SlotId != slotInfo.SlotId)
                    {
                        _lastNotifiedSlot = slotInfo;
                        Mediator.Publish(new NotificationMessage(Loc.Get("SlotPopup.Title"), string.Format(Loc.Get("Slot.Toast.Welcome"), slotInfo.SlotName), MareConfiguration.Models.NotificationType.Info));
                    }
                }
                else
                {
                    _lastNotifiedSlot = slotInfo;
                    Mediator.Publish(new OpenSlotPromptMessage(slotInfo));
                }
            }
        }
    }

    private void OnHousingPlotLeft()
    {
        Logger.LogInformation("Left housing plot");

        if (_lastNotifiedSlot != null)
        {
            var isMember = _pairManager.Groups.Any(g => _lastNotifiedSlot.AssociatedSyncshell != null && string.Equals(g.Key.GID, _lastNotifiedSlot.AssociatedSyncshell.Gid, StringComparison.Ordinal));
            if (isMember && !_joinedViaSlot)
            {
                Mediator.Publish(new NotificationMessage(Loc.Get("SlotPopup.Title"), string.Format(Loc.Get("Slot.Toast.Leaving"), _lastNotifiedSlot.SlotName), MareConfiguration.Models.NotificationType.Info));
            }
            _lastNotifiedSlot = null;
        }

        if (_currentSlotSyncshell != null && _joinedViaSlot)
        {
            Logger.LogInformation("Starting 5 minute leave timer for syncshell {name}", _currentSlotSyncshell.Name);
            _leaveTimerCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), _leaveTimerCts.Token).ConfigureAwait(false);
                    await LeaveSlotSyncshell().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Logger.LogInformation("Leave timer canceled");
                }
            }, _leaveTimerCts.Token);
        }
    }

    private async Task LeaveSlotSyncshell()
    {
        if (_currentSlotSyncshell != null)
        {
            Logger.LogInformation("Leaving slot syncshell {name} due to inactivity", _currentSlotSyncshell.Name);
            await _apiController.GroupLeave(new GroupDto(new GroupData(_currentSlotSyncshell.Gid))).ConfigureAwait(false);
            _currentSlotSyncshell = null;
        }
    }

    public void DeclineSlot(SlotInfoResponseDto slotInfo)
    {
        _declinedSlots.Add(slotInfo.SlotId);
        var msg = string.Format(Loc.Get("Slot.Decline.Notification"), slotInfo.SlotName);
        Mediator.Publish(new NotificationMessage(Loc.Get("SlotPopup.Title"), msg, MareConfiguration.Models.NotificationType.Info));
        _chatService.Print(msg);
    }

    public void Dispose()
    {
        _leaveTimerCts?.Cancel();
        _leaveTimerCts?.Dispose();
        UnsubscribeAll();
    }
}
