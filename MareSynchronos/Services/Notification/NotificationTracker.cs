using System;
using System.Collections.Generic;
using System.Linq;
using MareSynchronos.Services.Mediator;

namespace MareSynchronos.Services.Notifications;

public enum NotificationCategory
{
    AutoDetect,
}

public sealed record NotificationEntry(NotificationCategory Category, string Id, string Title, string? Description, DateTime CreatedAt)
{
    public static NotificationEntry AutoDetect(string uid, string displayName)
        => new(NotificationCategory.AutoDetect, uid, displayName, "Nouvelle demande d'appairage via AutoDetect.", DateTime.UtcNow);
}

public sealed class NotificationTracker
{
    private readonly MareMediator _mediator;
    private readonly Dictionary<(NotificationCategory Category, string Id), NotificationEntry> _entries = new();
    private readonly object _lock = new();

    public NotificationTracker(MareMediator mediator)
    {
        _mediator = mediator;
    }

    public void Upsert(NotificationEntry entry)
    {
        lock (_lock)
        {
            _entries[(entry.Category, entry.Id)] = entry;
        }
        PublishState();
    }

    public void Remove(NotificationCategory category, string id)
    {
        lock (_lock)
        {
            _entries.Remove((category, id));
        }
        PublishState();
    }

    public IReadOnlyList<NotificationEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.Values
                .OrderBy(e => e.CreatedAt)
                .ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    private void PublishState()
    {
        _mediator.Publish(new NotificationStateChanged(Count));
    }
}
