using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System.Collections.Generic;
using System.Linq;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace UmbraSync.Interop.Ipc;

public sealed class IpcCallerMare : DisposableMediatorSubscriberBase
{
    private const string ExternalUmbraInternalName = "Umbra";

    private readonly IDalamudPluginInterface _pi;
    private readonly ICallGateSubscriber<List<nint>> _mareHandledGameAddresses;
    private static readonly IReadOnlyList<nint> EmptyAddresses = Array.Empty<nint>();

    public IpcCallerMare(ILogger<IpcCallerMare> logger, IDalamudPluginInterface pi,  MareMediator mediator) : base(logger, mediator)
    {
        _pi = pi;
        _mareHandledGameAddresses = _pi.GetIpcSubscriber<List<nint>>($"{ExternalUmbraInternalName}.GetHandledAddresses");

        Mediator.SubscribeKeyed<PluginChangeMessage>(this, ExternalUmbraInternalName, _ => { });
    }

    public bool APIAvailable { get; private set; } = false;

    // Must be called on framework thread
    public IReadOnlyList<nint> GetHandledGameAddresses()
    {
        if (!IsExternalUmbraLoaded()) return EmptyAddresses;

        try
        {
            return _mareHandledGameAddresses.InvokeFunc();
        }
        catch
        {
            return EmptyAddresses;
        }
    }

    private bool IsExternalUmbraLoaded()
    {
        var plugin = _pi.InstalledPlugins.FirstOrDefault(p => p.InternalName.Equals(ExternalUmbraInternalName, StringComparison.Ordinal) && p.IsLoaded);
        if (plugin == null) return false;

        try
        {
            _pi.GetIpcSubscriber<List<nint>>($"{ExternalUmbraInternalName}.GetHandledAddresses").InvokeFunc();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
