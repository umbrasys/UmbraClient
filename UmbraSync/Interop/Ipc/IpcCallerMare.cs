using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace UmbraSync.Interop.Ipc;

public sealed class IpcCallerMare : DisposableMediatorSubscriberBase
{
    private readonly ICallGateSubscriber<List<nint>> _mareHandledGameAddresses;
    private readonly List<nint> _emptyList = [];

    private bool _pluginLoaded;

    public IpcCallerMare(ILogger<IpcCallerMare> logger, IDalamudPluginInterface pi,  MareMediator mediator) : base(logger, mediator)
    {
        _mareHandledGameAddresses = pi.GetIpcSubscriber<List<nint>>("UmbraSync.GetHandledAddresses");

        _pluginLoaded = PluginWatcherService.GetInitialPluginState(pi, "UmbraSync")?.IsLoaded ?? false;

        Mediator.SubscribeKeyed<PluginChangeMessage>(this, "UmbraSync", (msg) =>
        {
            _pluginLoaded = msg.IsLoaded;
        });
    }

    public bool APIAvailable { get; private set; } = false;

    // Must be called on framework thread
    public IReadOnlyList<nint> GetHandledGameAddresses()
    {
        if (!_pluginLoaded) return _emptyList;

        try
        {
            return _mareHandledGameAddresses.InvokeFunc();
        }
        catch
        {
            return _emptyList;
        }
    }
}
