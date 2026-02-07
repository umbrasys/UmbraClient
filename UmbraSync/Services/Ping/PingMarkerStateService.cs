using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;
using UmbraSync.API.Data;
using UmbraSync.API.Data.Enum;
using UmbraSync.API.Dto.Ping;
using UmbraSync.Services.Mediator;

namespace UmbraSync.Services.Ping;

public sealed class PingMarkerEntry
{
    public required PingMarkerDto Ping { get; init; }
    public required string SenderName { get; init; }
    public required string SenderUID { get; init; }
    public required string GroupGID { get; init; }
    public char Label { get; set; } = 'A';
    public float DrawDuration { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Vector3 WorldPosition => new(Ping.PositionX, Ping.PositionY, Ping.PositionZ);
}

public sealed class PingMarkerStateService : IMediatorSubscriber, IDisposable
{
    public const int MaxPingsPerUser = 8;
    public const int MaxPingsPerSyncshell = 32;

    private readonly ConcurrentDictionary<Guid, PingMarkerEntry> _markers = new();
    private readonly ILogger<PingMarkerStateService> _logger;

    public PingMarkerStateService(ILogger<PingMarkerStateService> logger, MareMediator mediator)
    {
        _logger = logger;
        Mediator = mediator;

        mediator.Subscribe<PingMarkerReceivedMessage>(this, OnPingReceived);
        mediator.Subscribe<PingMarkerRemovedMessage>(this, OnPingRemoved);
        mediator.Subscribe<PingMarkersClearedMessage>(this, OnPingsCleared);
        mediator.Subscribe<ZoneSwitchStartMessage>(this, _ => ClearAll());
    }

    public MareMediator Mediator { get; }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
    }

    public bool TryAddMarker(PingMarkerEntry entry)
    {
        var userCount = _markers.Values.Count(m =>
            string.Equals(m.SenderUID, entry.SenderUID, StringComparison.Ordinal)
            && string.Equals(m.GroupGID, entry.GroupGID, StringComparison.Ordinal));

        if (userCount >= MaxPingsPerUser)
        {
            _logger.LogWarning("PingMarkerStateService: max pings per user reached ({max}) for {uid}", MaxPingsPerUser, entry.SenderUID);
            return false;
        }

        var groupCount = _markers.Values.Count(m =>
            string.Equals(m.GroupGID, entry.GroupGID, StringComparison.Ordinal));

        if (groupCount >= MaxPingsPerSyncshell)
        {
            _logger.LogWarning("PingMarkerStateService: max pings per syncshell reached ({max}) for {gid}", MaxPingsPerSyncshell, entry.GroupGID);
            return false;
        }

        // Auto-assign next available letter for this user
        var usedLabels = _markers.Values
            .Where(m => string.Equals(m.SenderUID, entry.SenderUID, StringComparison.Ordinal)
                && m.Ping.TerritoryId == entry.Ping.TerritoryId)
            .Select(m => m.Label)
            .ToHashSet();
        var label = 'A';
        while (usedLabels.Contains(label) && label <= 'Z')
            label++;
        entry.Label = label;

        if (_markers.TryAdd(entry.Ping.Id, entry))
        {
            _logger.LogDebug("PingMarkerStateService: added ping {id} [{label}] from {sender} in {group}", entry.Ping.Id, label, entry.SenderName, entry.GroupGID);
            return true;
        }

        return false;
    }

    public bool TryRemoveMarker(Guid pingId)
    {
        if (_markers.TryRemove(pingId, out var removed))
        {
            _logger.LogDebug("PingMarkerStateService: removed ping {id} from {sender}", pingId, removed.SenderName);
            return true;
        }
        return false;
    }

    public void RemoveAllByUser(string uid, string groupGid)
    {
        var toRemove = _markers.Where(m =>
            string.Equals(m.Value.SenderUID, uid, StringComparison.Ordinal)
            && string.Equals(m.Value.GroupGID, groupGid, StringComparison.Ordinal))
            .Select(m => m.Key)
            .ToList();

        foreach (var id in toRemove)
        {
            _markers.TryRemove(id, out _);
        }

        _logger.LogDebug("PingMarkerStateService: removed {count} pings for user {uid} in {gid}", toRemove.Count, uid, groupGid);
    }

    public void ClearGroup(string groupGid)
    {
        var toRemove = _markers.Where(m =>
            string.Equals(m.Value.GroupGID, groupGid, StringComparison.Ordinal))
            .Select(m => m.Key)
            .ToList();

        foreach (var id in toRemove)
        {
            _markers.TryRemove(id, out _);
        }

        _logger.LogDebug("PingMarkerStateService: cleared {count} pings for group {gid}", toRemove.Count, groupGid);
    }

    public IReadOnlyCollection<PingMarkerEntry> GetMarkersForTerritory(uint territoryId)
    {
        return _markers.Values
            .Where(m => m.Ping.TerritoryId == territoryId)
            .ToList();
    }

    public IReadOnlyCollection<PingMarkerEntry> GetAllMarkers()
    {
        return _markers.Values.ToList();
    }

    public int GetUserPingCount(string uid, string groupGid)
    {
        return _markers.Values.Count(m =>
            string.Equals(m.SenderUID, uid, StringComparison.Ordinal)
            && string.Equals(m.GroupGID, groupGid, StringComparison.Ordinal));
    }

    public void ClearAll()
    {
        _markers.Clear();
        _logger.LogDebug("PingMarkerStateService: cleared all markers");
    }

    private void OnPingReceived(PingMarkerReceivedMessage msg)
    {
        var entry = new PingMarkerEntry
        {
            Ping = msg.Dto.Ping,
            SenderName = msg.Dto.Sender.AliasOrUID,
            SenderUID = msg.Dto.Sender.UID,
            GroupGID = msg.Dto.Group.GID,
        };

        TryAddMarker(entry);
    }

    private void OnPingRemoved(PingMarkerRemovedMessage msg)
    {
        TryRemoveMarker(msg.PingId);
    }

    private void OnPingsCleared(PingMarkersClearedMessage msg)
    {
        ClearGroup(msg.Group.GID);
    }
}
