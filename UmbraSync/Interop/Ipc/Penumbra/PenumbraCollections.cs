using Microsoft.Extensions.Logging;
using PenumbraEnum = global::Penumbra.Api.Enums;
using PenumbraIpc = global::Penumbra.Api.IpcSubscribers;

namespace UmbraSync.Interop.Ipc.Penumbra;

public sealed class PenumbraCollections
{
    private readonly PenumbraCore _core;
    private readonly PenumbraIpc.AssignTemporaryCollection _penumbraAssignTemporaryCollection;
    private readonly PenumbraIpc.CreateTemporaryCollection _penumbraCreateNamedTemporaryCollection;
    private readonly PenumbraIpc.GetCollection _penumbraGetCollection;
    private readonly PenumbraIpc.DeleteTemporaryCollection _penumbraRemoveTemporaryCollection;

    public PenumbraCollections(PenumbraCore core)
    {
        _core = core;

        // Initialiser les IPC de collections
        _penumbraCreateNamedTemporaryCollection = new PenumbraIpc.CreateTemporaryCollection(_core.PluginInterface);
        _penumbraRemoveTemporaryCollection = new PenumbraIpc.DeleteTemporaryCollection(_core.PluginInterface);
        _penumbraAssignTemporaryCollection = new PenumbraIpc.AssignTemporaryCollection(_core.PluginInterface);
        _penumbraGetCollection = new PenumbraIpc.GetCollection(_core.PluginInterface);
    }
    
    public async Task AssignTemporaryCollectionAsync(ILogger logger, Guid collName, int idx)
    {
        if (!_core.APIAvailable) return;

        await _core.DalamudUtil.RunOnFrameworkThread(() =>
        {
            var retAssign = _penumbraAssignTemporaryCollection.Invoke(collName, idx, forceAssignment: true);
            logger.LogTrace("Assigning Temp Collection {collName} to index {idx}, Success: {ret}", collName, idx, retAssign);
            return collName;
        }).ConfigureAwait(false);
    }
    
    public async Task<Guid> CreateTemporaryCollectionAsync(ILogger logger, string uid)
    {
        if (!_core.APIAvailable) return Guid.Empty;

        return await _core.DalamudUtil.RunOnFrameworkThread(() =>
        {
            Guid collId;
            var random = new Random();
            var collName = "UmbraSync_" + uid + random.Next().ToString();
            PenumbraEnum.PenumbraApiEc penEC = _penumbraCreateNamedTemporaryCollection.Invoke(uid + random.Next().ToString(), collName, out collId);
            logger.LogTrace("Creating Temp Collection {collName}, GUID: {collId}", collName, collId);
            if (penEC != PenumbraEnum.PenumbraApiEc.Success)
            {
                logger.LogError("Failed to create temporary collection for {collName} with error code {penEC}. Please include this line in any error reports", collName, penEC);
                return Guid.Empty;
            }
            return collId;

        }).ConfigureAwait(false);
    }
    
    public async Task RemoveTemporaryCollectionAsync(ILogger logger, Guid applicationId, Guid collId)
    {
        if (!_core.APIAvailable) return;
        await _core.DalamudUtil.RunOnFrameworkThread(() =>
        {
            RemoveTemporaryCollection(logger, applicationId, collId);
        }).ConfigureAwait(false);
    }
    
    public void RemoveTemporaryCollection(ILogger logger, Guid applicationId, Guid collId)
    {
        if (!_core.APIAvailable) return;
        logger.LogTrace("[{applicationId}] Removing temp collection for {collId}", applicationId, collId);
        var ret2 = _penumbraRemoveTemporaryCollection.Invoke(collId);
        logger.LogTrace("[{applicationId}] RemoveTemporaryCollection: {ret2}", applicationId, ret2);
    }
    
    public (Guid id, string name)? GetCurrentCollection()
    {
        if (!_core.APIAvailable) return null;

        var coll = _penumbraGetCollection.Invoke(PenumbraEnum.ApiCollectionType.Current);
        if (coll == null) return null;

        return (coll.Value.Id, coll.Value.Name);
    }
}
