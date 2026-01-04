using Microsoft.Extensions.Logging;
using PenumbraIpc = global::Penumbra.Api.IpcSubscribers;
namespace UmbraSync.Interop.Ipc.Penumbra;
public sealed class PenumbraTemporaryMods
{
    private readonly PenumbraCore _core;
    private readonly PenumbraIpc.AddTemporaryMod _penumbraAddTemporaryMod;
    private readonly PenumbraIpc.RemoveTemporaryMod _penumbraRemoveTemporaryMod;

    public PenumbraTemporaryMods(PenumbraCore core)
    {
        _core = core;

        // Initialiser les IPC de temporary mods
        _penumbraRemoveTemporaryMod = new PenumbraIpc.RemoveTemporaryMod(_core.PluginInterface);
        _penumbraAddTemporaryMod = new PenumbraIpc.AddTemporaryMod(_core.PluginInterface);
    }
    
    public async Task SetManipulationDataAsync(ILogger logger, Guid applicationId, Guid collId, string manipulationData)
    {
        if (!_core.APIAvailable) return;

        await _core.DalamudUtil.RunOnFrameworkThread(() =>
        {
            logger.LogTrace("[{applicationId}] Manip: {data}", applicationId, manipulationData);
            var retAdd = _penumbraAddTemporaryMod.Invoke("MareChara_Meta", collId, [], manipulationData, 0);
            logger.LogTrace("[{applicationId}] Setting temp meta mod for {collId}, Success: {ret}", applicationId, collId, retAdd);
        }).ConfigureAwait(false);
    }
    public async Task SetTemporaryModsAsync(ILogger logger, Guid applicationId, Guid collId, Dictionary<string, string> modPaths)
    {
        if (!_core.APIAvailable) return;

        await _core.DalamudUtil.RunOnFrameworkThread(() =>
        {
            foreach (var mod in modPaths)
            {
                logger.LogTrace("[{applicationId}] Change: {from} => {to}", applicationId, mod.Key, mod.Value);
            }
            var retRemove = _penumbraRemoveTemporaryMod.Invoke("MareChara_Files", collId, 0);
            logger.LogTrace("[{applicationId}] Removing temp files mod for {collId}, Success: {ret}", applicationId, collId, retRemove);
            var retAdd = _penumbraAddTemporaryMod.Invoke("MareChara_Files", collId, modPaths, string.Empty, 0);
            logger.LogTrace("[{applicationId}] Setting temp files mod for {collId}, Success: {ret}", applicationId, collId, retAdd);
        }).ConfigureAwait(false);
    }
}
