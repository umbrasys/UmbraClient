using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using UmbraSync.Interop.Ipc;
using UmbraSync.Services.Mediator;

namespace UmbraSync.Services;

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
    private readonly HashSet<string> _makeVisibleNextFrame = new(StringComparer.Ordinal);
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
        Mediator.Subscribe<DisconnectedMessage>(this, (_) =>
        {
            _trackedPlayerVisibility.Clear();
            _makeVisibleNextFrame.Clear();
        });
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
            var isPresent = findResult.ObjectId != 0; // presence in object table

            // Transitions
            switch (player.Value)
            {
                case TrackedPlayerStatus.NotVisible:
                    if (isPresent)
                    {
                        if (_makeVisibleNextFrame.Contains(ident))
                        {
                            if (isMareHandled)
                            {
                                if (_trackedPlayerVisibility.TryUpdate(ident, TrackedPlayerStatus.MareHandled, TrackedPlayerStatus.NotVisible))
                                    Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: true, Invalidate: true));
                            }
                            else
                            {
                                if (_trackedPlayerVisibility.TryUpdate(ident, TrackedPlayerStatus.Visible, TrackedPlayerStatus.NotVisible))
                                    Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: true));
                            }
                        }
                        else
                        {
                            _makeVisibleNextFrame.Add(ident);
                        }
                    }
                    break;
                case TrackedPlayerStatus.Visible:
                    if (!isPresent &&
                        _trackedPlayerVisibility.TryUpdate(ident, TrackedPlayerStatus.NotVisible, TrackedPlayerStatus.Visible))
                    {
                        Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: false));
                    }
                    else if (isMareHandled &&
                             _trackedPlayerVisibility.TryUpdate(ident, TrackedPlayerStatus.MareHandled, TrackedPlayerStatus.Visible))
                    {
                        Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: true, Invalidate: true));
                    }
                    break;
                case TrackedPlayerStatus.MareHandled:
                    if (!isPresent &&
                        _trackedPlayerVisibility.TryUpdate(ident, TrackedPlayerStatus.NotVisible, TrackedPlayerStatus.MareHandled))
                    {
                        Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: false));
                    }
                    else if (!isMareHandled &&
                             _trackedPlayerVisibility.TryUpdate(ident, TrackedPlayerStatus.Visible, TrackedPlayerStatus.MareHandled))
                    {
                        // Became unhandled by Mare while still present -> visible to us
                        Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: true));
                    }
                    break;
            }

            if (!isPresent)
                _makeVisibleNextFrame.Remove(ident);
        }
    }
}