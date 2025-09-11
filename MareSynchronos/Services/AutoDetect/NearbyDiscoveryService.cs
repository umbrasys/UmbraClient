using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using MareSynchronos.Services.Mediator;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI.AutoDetect;
using Dalamud.Plugin.Services;
using System.Numerics;
using System.Linq;
using MareSynchronos.Utils;

namespace MareSynchronos.Services.AutoDetect;

public class NearbyDiscoveryService : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<NearbyDiscoveryService> _logger;
    private readonly MareMediator _mediator;
    private readonly MareConfigService _config;
    private readonly DiscoveryConfigProvider _configProvider;
    private readonly DalamudUtilService _dalamud;
    private readonly IObjectTable _objectTable;
    private readonly DiscoveryApiClient _api;
    private CancellationTokenSource? _loopCts;
    private string? _lastPublishedSignature;
    private bool _loggedLocalOnly;
    private int _lastLocalCount = -1;
    private int _lastMatchCount = -1;
    private bool _loggedConfigReady;
    private string? _lastSnapshotSig;
    private volatile bool _isConnected;

    public NearbyDiscoveryService(ILogger<NearbyDiscoveryService> logger, MareMediator mediator,
        MareConfigService config, DiscoveryConfigProvider configProvider, DalamudUtilService dalamudUtilService,
        IObjectTable objectTable, DiscoveryApiClient api)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;
        _configProvider = configProvider;
        _dalamud = dalamudUtilService;
        _objectTable = objectTable;
        _api = api;
    }

    public MareMediator Mediator => _mediator;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _loopCts = new CancellationTokenSource();
        _mediator.Subscribe<ConnectedMessage>(this, _ => { _isConnected = true; _configProvider.TryLoadFromStapled(); });
        _mediator.Subscribe<DisconnectedMessage>(this, _ => { _isConnected = false; _lastPublishedSignature = null; });
        _ = Task.Run(() => Loop(_loopCts.Token));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _mediator.UnsubscribeAll(this);
        try { _loopCts?.Cancel(); } catch { }
        return Task.CompletedTask;
    }

    private async Task Loop(CancellationToken ct)
    {
        // best effort config load
        _configProvider.TryLoadFromStapled();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_config.Current.EnableAutoDetectDiscovery || !_dalamud.IsLoggedIn || !_isConnected)
                {
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                    continue;
                }

                // Ensure we have a valid Nearby config: try stapled, then HTTP fallback
                if (!_configProvider.HasConfig || _configProvider.IsExpired())
                {
                    if (!_configProvider.TryLoadFromStapled())
                    {
                        await _configProvider.TryFetchFromServerAsync(ct).ConfigureAwait(false);
                    }
                }
                else if (!_loggedConfigReady && _configProvider.NearbyEnabled)
                {
                    _loggedConfigReady = true;
                    _logger.LogInformation("Nearby: well-known loaded and enabled; refresh={refresh}s, expires={exp}", _configProvider.RefreshSec, _configProvider.SaltExpiresAt);
                }

                var entries = await GetLocalNearbyAsync().ConfigureAwait(false);
                // Log when local count changes (including 0) to indicate activity
                if (entries.Count != _lastLocalCount)
                {
                    _lastLocalCount = entries.Count;
                    _logger.LogInformation("Nearby: {count} players detected locally", _lastLocalCount);
                }

                // Try query server if config and endpoints are present
                if (_configProvider.NearbyEnabled && !_configProvider.IsExpired() &&
                    _configProvider.Salt is { Length: > 0 })
                {
                    try
                    {
                        var saltHex = Convert.ToHexString(_configProvider.Salt!);
                        // map hash->index for result matching
                        Dictionary<string, int> hashToIndex = new(StringComparer.Ordinal);
                        List<string> hashes = new(entries.Count);
                        foreach (var (entry, idx) in entries.Select((e, i) => (e, i)))
                        {
                            var h = (saltHex + entry.Name + entry.WorldId.ToString()).GetHash256();
                            hashToIndex[h] = idx;
                            hashes.Add(h);
                        }

                        // Debug snapshot once per change
                        try
                        {
                            var snapSig = string.Join(',', hashes.OrderBy(s => s, StringComparer.Ordinal)).GetHash256();
                            if (!string.Equals(snapSig, _lastSnapshotSig, StringComparison.Ordinal))
                            {
                                _lastSnapshotSig = snapSig;
                                var sample = entries.Take(5).Select(e =>
                                {
                                    var hh = (saltHex + e.Name + e.WorldId.ToString()).GetHash256();
                                    var shortH = hh.Length > 8 ? hh[..8] : hh;
                                    return $"{e.Name}({e.WorldId})->{shortH}";
                                });
                                var saltShort = saltHex.Length > 8 ? saltHex[..8] : saltHex;
                                _logger.LogInformation("Nearby snapshot: {count} entries; salt={saltShort}…; samples=[{samples}]",
                                    entries.Count, saltShort, string.Join(", ", sample));
                            }
                        }
                        catch { }

                        // Publish OUR presence (own hash) if endpoint is available (deduplicated)
                        if (!string.IsNullOrEmpty(_configProvider.PublishEndpoint))
                        {
                            string? displayName = null;
                            string? selfHash = null;
                            try
                            {
                                var me = await _dalamud.RunOnFrameworkThread(() => _dalamud.GetPlayerCharacter()).ConfigureAwait(false);
                                if (me != null)
                                {
                                    displayName = me.Name.TextValue;
                                    ushort meWorld = 0;
                                    if (me is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter mePc)
                                        meWorld = (ushort)mePc.HomeWorld.RowId;
                                    _logger.LogInformation("Nearby self ident: {name} ({world})", displayName, meWorld);
                                    selfHash = (saltHex + displayName + meWorld.ToString()).GetHash256();
                                }
                            }
                            catch { /* ignore */ }

                            if (!string.IsNullOrEmpty(selfHash))
                            {
                                var sig = selfHash!;
                                if (!string.Equals(sig, _lastPublishedSignature, StringComparison.Ordinal))
                                {
                                    _lastPublishedSignature = sig;
                                    var shortSelf = selfHash!.Length > 8 ? selfHash[..8] : selfHash;
                                    _logger.LogInformation("Nearby publish: self presence updated (hash={hash})", shortSelf);
                                    var ok = await _api.PublishAsync(_configProvider.PublishEndpoint!, new[] { selfHash! }, displayName, ct).ConfigureAwait(false);
                                    _logger.LogInformation("Nearby publish result: {result}", ok ? "success" : "failed");
                                }
                                else
                                {
                                    _logger.LogDebug("Nearby publish skipped (no changes)");
                                }
                            }
                            // else: no self character available; skip publish silently
                        }

                        // Query for matches if endpoint is available
                        if (!string.IsNullOrEmpty(_configProvider.QueryEndpoint))
                        {
                            // chunked queries
                            int batch = Math.Max(1, _configProvider.MaxQueryBatch);
                            List<ServerMatch> allMatches = new();
                            for (int i = 0; i < hashes.Count; i += batch)
                            {
                                var slice = hashes.Skip(i).Take(batch).ToArray();
                                var res = await _api.QueryAsync(_configProvider.QueryEndpoint!, slice, ct).ConfigureAwait(false);
                                if (res != null && res.Count > 0) allMatches.AddRange(res);
                            }

                            if (allMatches.Count > 0)
                            {
                                foreach (var m in allMatches)
                                {
                                    if (hashToIndex.TryGetValue(m.Hash, out var idx))
                                    {
                                        var e = entries[idx];
                                        entries[idx] = new NearbyEntry(e.Name, e.WorldId, e.Distance, true, m.Token);
                                    }
                                }
                            }
                            _logger.LogInformation("Nearby: server returned {count} matches", allMatches.Count);

                            // Log change in number of Umbra matches
                            int matchCount = entries.Count(e => e.IsMatch);
                            if (matchCount != _lastMatchCount)
                            {
                                _lastMatchCount = matchCount;
                                _logger.LogInformation("Nearby: {count} Umbra users nearby", matchCount);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Nearby query failed; falling back to local list");
                    }
                }
                else
                {
                    if (!_loggedLocalOnly)
                    {
                        _loggedLocalOnly = true;
                        _logger.LogInformation("Nearby: well-known not available or disabled; running in local-only mode");
                    }
                }
                _mediator.Publish(new DiscoveryListUpdated(entries));

                var delayMs = Math.Max(1000, _configProvider.MinQueryIntervalMs);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "NearbyDiscoveryService loop error");
                await Task.Delay(2000, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<List<NearbyEntry>> GetLocalNearbyAsync()
    {
        var list = new List<NearbyEntry>();
        try
        {
            var local = await _dalamud.RunOnFrameworkThread(() => _dalamud.GetPlayerCharacter()).ConfigureAwait(false);
            var localPos = local?.Position ?? Vector3.Zero;
            int maxDist = Math.Clamp(_config.Current.AutoDetectMaxDistanceMeters, 5, 100);

            int limit = Math.Min(200, _objectTable.Length);
            for (int i = 0; i < limit; i++)
            {
                var obj = await _dalamud.RunOnFrameworkThread(() => _objectTable[i]).ConfigureAwait(false);
                if (obj == null || obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
                if (local != null && obj.Address == local.Address) continue;

                float dist = local == null ? float.NaN : Vector3.Distance(localPos, obj.Position);
                if (!float.IsNaN(dist) && dist > maxDist) continue;

                string name = obj.Name.TextValue;
                ushort worldId = 0;
                if (obj is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc)
                    worldId = (ushort)pc.HomeWorld.RowId;

                list.Add(new NearbyEntry(name, worldId, dist, false, null));
            }
        }
        catch
        {
            // ignore
        }
        return list;
    }
}
