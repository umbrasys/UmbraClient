using Microsoft.Extensions.Logging;
using UmbraSync.API.Data;
using UmbraSync.Services.Mediator;
namespace UmbraSync.PlayerData.Pairs;

public sealed class PairLedger : DisposableMediatorSubscriberBase
{
    private readonly PairManager _pairManager;
    private readonly PairStateCache _stateCache;
    private readonly Lock _metricsGate = new();
    private CancellationTokenSource? _ensureMetricsCts;

    public PairLedger(
        ILogger<PairLedger> logger,
        MareMediator mediator,
        PairManager pairManager,
        PairStateCache stateCache) : base(logger, mediator)
    {
        _pairManager = pairManager;
        _stateCache = stateCache;

        // Événements déclenchant une réapplication
        Mediator.Subscribe<CutsceneEndMessage>(this, _ => ReapplyAll(forced: true));
        Mediator.Subscribe<GposeEndMessage>(this, _ => ReapplyAll());
        Mediator.Subscribe<PenumbraInitializedMessage>(this, _ => ReapplyAll(forced: true));
        Mediator.Subscribe<ConnectedMessage>(this, _ =>
        {
            ReapplyAll(forced: true);
            ScheduleEnsureMetrics(TimeSpan.FromSeconds(2));
        });

        // Événements pour les métriques
        Mediator.Subscribe<HubReconnectedMessage>(this, _ => ScheduleEnsureMetrics(TimeSpan.FromSeconds(2)));
        Mediator.Subscribe<DalamudLoginMessage>(this, _ => ScheduleEnsureMetrics(TimeSpan.FromSeconds(2)));

        // Nettoyage à la déconnexion
        Mediator.Subscribe<DisconnectedMessage>(this, _ => Reset());
    }
    
    // Vérifie si une paire est visible.
    public bool IsPairVisible(string visibleUid)
    {
        var pair = _pairManager.GetPairByUID(visibleUid);
        return pair?.IsVisible ?? false;
    }
    
    // Récupère toutes les paires visibles.
    public IReadOnlyList<Pair> GetVisiblePairs()
    {
        return _pairManager.GetOnlineUserPairs()
            .Where(p => p.IsVisible)
            .ToList();
    }
    
    // Récupère le nombre de paires visibles.
    public int GetVisiblePairCount() => _pairManager.GetVisibleUserCount();
    
    // Réapplique les données pour toutes les paires.

    public void ReapplyAll(bool forced = false)
    {
        if (Logger.IsEnabled(LogLevel.Trace))
            Logger.LogTrace("Réapplication des données pour toutes les paires (forced: {Forced})", forced);

        foreach (var pair in _pairManager.GetOnlineUserPairs())
        {
            pair.ApplyLastReceivedData(forced);
        }
    }
    
    // Réapplique les données pour une paire spécifique.
    public void ReapplyPair(string uid, bool forced = false)
    {
        var pair = _pairManager.GetPairByUID(uid);
        if (pair == null)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
                Logger.LogDebug("Impossible de réappliquer: paire {Uid} non trouvée", uid);
            return;
        }

        pair.ApplyLastReceivedData(forced);
    }
    
    // Stocke les données de personnage dans le cache.
    public void CacheCharacterData(string ident, CharacterData data)
    {
        _stateCache.Store(ident, data);
    }
    
    // Récupère les données de personnage depuis le cache.
    public CharacterData? GetCachedCharacterData(string ident)
    {
        return _stateCache.TryLoad(ident);
    }


    // Stocke une collection temporaire Penumbra dans le cache.
    
    public void CacheTemporaryCollection(string ident, Guid collectionId)
    {
        _stateCache.StoreTemporaryCollection(ident, collectionId);
    }
    
    // Récupère une collection temporaire Penumbra depuis le cache.

    public Guid? GetCachedTemporaryCollection(string ident)
    {
        return _stateCache.TryGetTemporaryCollection(ident);
    }


    // Supprime une collection temporaire du cache.

    public Guid? ClearCachedTemporaryCollection(string ident)
    {
        return _stateCache.ClearTemporaryCollection(ident);
    }


    // Supprime toutes les collections temporaires du cache.

    public IReadOnlyList<Guid> ClearAllCachedTemporaryCollections()
    {
        return _stateCache.ClearAllTemporaryCollections();
    }

    private void Reset()
    {
        if (Logger.IsEnabled(LogLevel.Trace))
            Logger.LogTrace("Reset du PairLedger après déconnexion");

        CancelScheduledMetrics();
        _stateCache.ClearAll();
    }

    private void ScheduleEnsureMetrics(TimeSpan? delay = null)
    {
        lock (_metricsGate)
        {
            _ensureMetricsCts?.Cancel();
            var cts = new CancellationTokenSource();
            _ensureMetricsCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (delay is { } d && d > TimeSpan.Zero)
                        await Task.Delay(d, cts.Token).ConfigureAwait(false);

                    EnsureMetricsForVisiblePairs();
                }
                catch (OperationCanceledException)
                {
                    // ignoré
                }
                finally
                {
                    lock (_metricsGate)
                    {
                        if (_ensureMetricsCts == cts)
                            _ensureMetricsCts = null;
                    }
                    cts.Dispose();
                }
            });
        }
    }

    private void CancelScheduledMetrics()
    {
        lock (_metricsGate)
        {
            _ensureMetricsCts?.Cancel();
            _ensureMetricsCts = null;
        }
    }

    private void EnsureMetricsForVisiblePairs()
    {
        foreach (var pair in GetVisiblePairs())
        {
            if (pair.LastReceivedCharacterData is null)
                continue;

            // Si les métriques sont déjà présentes, pas besoin de réappliquer
            if (pair.LastAppliedApproximateVRAMBytes >= 0 && pair.LastAppliedDataTris >= 0)
                continue;

            try
            {
                pair.ApplyLastReceivedData(forced: true);
            }
            catch (Exception ex)
            {
                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug(ex, "Échec de l'assurance des métriques pour {Ident}", pair.Ident);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            CancelScheduledMetrics();

        base.Dispose(disposing);
    }
}
