using MareSynchronos.Interop.Ipc;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.Services;

// Detect when players of interest are visible
public class VisibilityService : DisposableMediatorSubscriberBase
{
    private enum TrackedPlayerStatus
    {
        NotVisible,
        Visible,
        MareHandled
    };

    private readonly DalamudUtilService _dalamudUtil;
    private readonly ConcurrentDictionary<string, TrackedPlayerStatus> _trackedPlayerVisibility = new(StringComparer.Ordinal);
    private readonly List<string> _makeVisibleNextFrame = new();
    private readonly IpcCallerMare _mare;
    private readonly HashSet<nint> cachedMareAddresses = new();
    private uint _cachedAddressSum = 0;
    private uint _cachedAddressSumDebounce = 1;

    public VisibilityService(ILogger<VisibilityService> logger, MareMediator mediator, IpcCallerMare mare, DalamudUtilService dalamudUtil)
        : base(logger, mediator)
    {
        _mare = mare;
        _dalamudUtil = dalamudUtil;
        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => FrameworkUpdate());
    }

    public void StartTracking(string ident)
    {
        _trackedPlayerVisibility.TryAdd(ident, TrackedPlayerStatus.NotVisible);
    }

    public void StopTracking(string ident)
    {
        // No PairVisibilityMessage is emitted if the player was visible when removed
        _trackedPlayerVisibility.TryRemove(ident, out _);
    }

    private void FrameworkUpdate()
    {
        var mareHandledAddresses = _mare.GetHandledGameAddresses();
        uint addressSum = 0;

        foreach (var addr in mareHandledAddresses)
            addressSum ^= (uint)addr.GetHashCode();

        if (addressSum != _cachedAddressSum)
        {
            if (addressSum == _cachedAddressSumDebounce)
            {
                cachedMareAddresses.Clear();
                foreach (var addr in mareHandledAddresses)
                    cachedMareAddresses.Add(addr);
                _cachedAddressSum = addressSum;
            }
            else
            {
                _cachedAddressSumDebounce = addressSum;
            }
        }

        foreach (var player in _trackedPlayerVisibility)
        {
            string ident = player.Key;
            var findResult = _dalamudUtil.FindPlayerByNameHash(ident);
            var isMareHandled = cachedMareAddresses.Contains(findResult.Address);
            var isVisible = findResult.ObjectId != 0 && !isMareHandled;

            if (player.Value == TrackedPlayerStatus.MareHandled && !isMareHandled)
                _trackedPlayerVisibility.TryUpdate(ident, newValue: TrackedPlayerStatus.NotVisible, comparisonValue: TrackedPlayerStatus.MareHandled);

            if (player.Value == TrackedPlayerStatus.NotVisible && isVisible)
            {
                if (_makeVisibleNextFrame.Contains(ident))
                {
                    if (_trackedPlayerVisibility.TryUpdate(ident, newValue: TrackedPlayerStatus.Visible, comparisonValue: TrackedPlayerStatus.NotVisible))
                        Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: true));
                }
                else
                    _makeVisibleNextFrame.Add(ident);
            }
            else if (player.Value == TrackedPlayerStatus.NotVisible && isMareHandled)
            {
                // Send a technically redundant visibility update with the added intent of triggering PairHandler to undo the application by name
                if (_trackedPlayerVisibility.TryUpdate(ident, newValue: TrackedPlayerStatus.MareHandled, comparisonValue: TrackedPlayerStatus.NotVisible))
                    Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: false, Invalidate: true));
            }
            else if (player.Value == TrackedPlayerStatus.Visible && !isVisible)
            {
                var newTrackedStatus = isMareHandled ? TrackedPlayerStatus.MareHandled : TrackedPlayerStatus.NotVisible;
                if (_trackedPlayerVisibility.TryUpdate(ident, newValue: newTrackedStatus, comparisonValue: TrackedPlayerStatus.Visible))
                    Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: false, Invalidate: isMareHandled));
            }

            if (!isVisible)
                _makeVisibleNextFrame.Remove(ident);
        }
    }
}