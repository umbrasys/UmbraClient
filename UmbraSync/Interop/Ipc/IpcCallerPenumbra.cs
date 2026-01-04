using Dalamud.Plugin;
using Microsoft.Extensions.Logging;
using UmbraSync.Interop.Ipc.Penumbra;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.PlayerData.Handlers;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.Notification;

// Aliases pour les IPC qui restent dans la facade
using PenumbraIpc = global::Penumbra.Api.IpcSubscribers;

namespace UmbraSync.Interop.Ipc;

/// <summary>
/// Facade pour l'intégration Penumbra.
/// Délègue aux modules spécialisés pour une architecture modulaire et maintenable.
/// Architecture inspirée de Lightless mais avec les innovations UmbraSync (debouncing, etc.)
/// </summary>
public sealed class IpcCallerPenumbra : DisposableMediatorSubscriberBase, IIpcCaller
{
    // Modules spécialisés
    private readonly PenumbraCore _core;
    private readonly PenumbraModSettings _modSettings;
    private readonly PenumbraRedraw _redraw;
    private readonly PenumbraCollections _collections;
    private readonly PenumbraResources _resources;
    private readonly PenumbraTextures _textures;
    private readonly PenumbraTemporaryMods _temporaryMods;

    // IPC non déléguées (spécifiques à la facade)
    private readonly PenumbraIpc.ResolvePlayerPathsAsync _penumbraResolvePaths;
    private readonly PenumbraIpc.GetPlayerMetaManipulations _penumbraGetMetaManipulations;

    public IpcCallerPenumbra(
        ILogger<IpcCallerPenumbra> logger,
        IDalamudPluginInterface pi,
        DalamudUtilService dalamudUtil,
        MareMediator mareMediator,
        RedrawManager redrawManager,
        NotificationTracker notificationTracker) : base(logger, mareMediator)
    {
        // Initialiser le core
        _core = new PenumbraCore(logger, pi, dalamudUtil, mareMediator, redrawManager, notificationTracker);

        // Initialiser les modules spécialisés
        _modSettings = new PenumbraModSettings(_core);
        _redraw = new PenumbraRedraw(_core);
        _collections = new PenumbraCollections(_core);
        _resources = new PenumbraResources(_core);
        _textures = new PenumbraTextures(_core);
        _temporaryMods = new PenumbraTemporaryMods(_core);

        // IPC spécifiques à la facade
        _penumbraResolvePaths = new PenumbraIpc.ResolvePlayerPathsAsync(pi);
        _penumbraGetMetaManipulations = new PenumbraIpc.GetPlayerMetaManipulations(pi);

        // S'abonner au message de redraw de personnage
        Mediator.Subscribe<PenumbraRedrawCharacterMessage>(this, (msg) =>
        {
            _redraw.RedrawCharacter(msg.Character);
        });
    }

    // === Propriétés déléguées au Core ===

    public bool APIAvailable => _core.APIAvailable;

    public string? ModDirectory => _core.ModDirectory;

    // === Méthodes déléguées au Core ===

    public void CheckAPI() => _core.CheckAPI();

    public void CheckModDirectory() => _core.CheckModDirectory();

    // === Méthodes déléguées à ModSettings ===

    public Task<HashSet<string>?> GetEnabledModRootsAsync() => _modSettings.GetEnabledModRootsAsync();

    // === Méthodes déléguées à Redraw ===

    public Task RedrawAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, CancellationToken token)
        => _redraw.RedrawAsync(logger, handler, applicationId, token);

    public void RedrawNow(ILogger logger, Guid applicationId, int objectIndex)
        => _redraw.RedrawNow(logger, applicationId, objectIndex);

    // === Méthodes déléguées à Collections ===

    public Task AssignTemporaryCollectionAsync(ILogger logger, Guid collName, int idx)
        => _collections.AssignTemporaryCollectionAsync(logger, collName, idx);

    public Task<Guid> CreateTemporaryCollectionAsync(ILogger logger, string uid)
        => _collections.CreateTemporaryCollectionAsync(logger, uid);

    public Task RemoveTemporaryCollectionAsync(ILogger logger, Guid applicationId, Guid collId)
        => _collections.RemoveTemporaryCollectionAsync(logger, applicationId, collId);

    public void RemoveTemporaryCollection(ILogger logger, Guid applicationId, Guid collId)
        => _collections.RemoveTemporaryCollection(logger, applicationId, collId);

    // === Méthodes déléguées à Resources ===

    public Task<Dictionary<string, HashSet<string>>?> GetCharacterData(ILogger logger, GameObjectHandler handler)
        => _resources.GetCharacterData(logger, handler);

    // === Méthodes déléguées à Textures ===

    public Task ConvertTextureFiles(ILogger logger, Dictionary<string, string[]> textures, IProgress<(string, int)> progress, CancellationToken token)
        => _textures.ConvertTextureFiles(logger, textures, progress, token);

    // === Méthodes déléguées à TemporaryMods ===

    public Task SetManipulationDataAsync(ILogger logger, Guid applicationId, Guid collId, string manipulationData)
        => _temporaryMods.SetManipulationDataAsync(logger, applicationId, collId, manipulationData);

    public Task SetTemporaryModsAsync(ILogger logger, Guid applicationId, Guid collId, Dictionary<string, string> modPaths)
        => _temporaryMods.SetTemporaryModsAsync(logger, applicationId, collId, modPaths);

    // === Méthodes spécifiques à la facade (non déléguées) ===

    /// <summary>
    /// Résout les chemins de fichiers via Penumbra (forward et reverse).
    /// </summary>
    public async Task<(string[] forward, string[][] reverse)> ResolvePathsAsync(string[] forward, string[] reverse)
    {
        if (!APIAvailable) return ([], []);

        Logger.LogTrace("Calling on IPC: Penumbra.ResolvePlayerPaths");
        return await _penumbraResolvePaths.Invoke(forward, reverse).ConfigureAwait(false);
    }

    /// <summary>
    /// Récupère les manipulations metadata du joueur.
    /// </summary>
    public string GetMetaManipulations()
    {
        return APIAvailable ? _penumbraGetMetaManipulations.Invoke() : string.Empty;
    }

    // === Dispose ===

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _core.Dispose();
            _modSettings.Dispose();
            _redraw.Dispose();
            _resources.Dispose();
        }
    }
}
