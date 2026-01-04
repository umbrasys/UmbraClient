using Dalamud.Plugin;
using Microsoft.Extensions.Logging;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;

namespace UmbraSync.Interop.Ipc.Penumbra;

public interface IPenumbraComponent
{

    ILogger Logger { get; }
    IDalamudPluginInterface PluginInterface { get; }
    DalamudUtilService DalamudUtil { get; }
    MareMediator Mediator { get; }
}
