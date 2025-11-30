using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Globalization;
using UmbraSync.Localization;
using UmbraSync.Services.Mediator;
using UmbraSync.MareConfiguration;
using UmbraSync.MareConfiguration.Configurations;
using UmbraSync.MareConfiguration.Models;

namespace UmbraSync.Services.Notification;

public enum NotificationCategory
{
    AutoDetect,
    Syncshell,
    McdfShare,
}

public sealed record NotificationEntry(NotificationCategory Category, string Id, string Title, string? Description, DateTime CreatedAt)
{
    public static NotificationEntry AutoDetect(string uid, string displayName)
    {
        var title = string.Format(CultureInfo.CurrentCulture, Loc.Get("Notification.AutoDetect.RequestTitle"), displayName);
        var desc = string.Format(CultureInfo.CurrentCulture, Loc.Get("Notification.AutoDetect.RequestBody"), displayName, uid);
        return new(NotificationCategory.AutoDetect, uid, title, desc, DateTime.UtcNow);
    }

    public static NotificationEntry SyncshellPublic(string gid, string aliasOrGid)
        => new(NotificationCategory.Syncshell, gid,
            string.Format(CultureInfo.CurrentCulture, Loc.Get("Notification.Syncshell.Public.Title"), aliasOrGid),
            Loc.Get("Notification.Syncshell.Public.Body"), DateTime.UtcNow);

    public static NotificationEntry SyncshellNotPublic(string gid, string aliasOrGid)
        => new(NotificationCategory.Syncshell, gid,
            string.Format(CultureInfo.CurrentCulture, Loc.Get("Notification.Syncshell.NotPublic.Title"), aliasOrGid),
            Loc.Get("Notification.Syncshell.NotPublic.Body"), DateTime.UtcNow);

    public static NotificationEntry McdfShareCreated(Guid shareId, string? description, int individualCount, int syncshellCount)
    {
        string safeDescription = string.IsNullOrEmpty(description)
            ? shareId.ToString("D", CultureInfo.InvariantCulture)
            : description;
        string title = string.Format(CultureInfo.CurrentCulture, Loc.Get("Notification.McdfShare.Created.Title"), safeDescription);
        string targetSummary = string.Format(CultureInfo.CurrentCulture, Loc.Get("Notification.McdfShare.Created.Summary"), individualCount, syncshellCount);
        return new(NotificationCategory.McdfShare, shareId.ToString("D", CultureInfo.InvariantCulture), title, targetSummary, DateTime.UtcNow);
    }
}

public sealed class NotificationTracker
{
    private const int MaxStored = 100;

    private readonly MareMediator _mediator;
    private readonly NotificationsConfigService _configService;
    private readonly Dictionary<(NotificationCategory Category, string Id), NotificationEntry> _entries = new();
    private readonly Lock _lock = new();

    public NotificationTracker(MareMediator mediator, NotificationsConfigService configService)
    {
        _mediator = mediator;
        _configService = configService;
        LoadPersisted();
        PublishState();
    }

    public void Upsert(NotificationEntry entry)
    {
        using (_lock.EnterScope())
        {
            _entries[(entry.Category, entry.Id)] = entry;
            TrimIfNecessary_NoLock();
            Persist_NoLock();
        }
        PublishState();
    }

    public void Remove(NotificationCategory category, string id)
    {
        using (_lock.EnterScope())
        {
            _entries.Remove((category, id));
            Persist_NoLock();
        }
        PublishState();
    }

    public IReadOnlyList<NotificationEntry> GetEntries()
    {
        using (_lock.EnterScope())
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
            using (_lock.EnterScope())
            {
                return _entries.Count;
            }
        }
    }

    private void PublishState()
    {
        _mediator.Publish(new NotificationStateChanged(Count));
    }

    private void LoadPersisted()
    {
        try
        {
            var list = _configService.Current.Notifications ?? new List<StoredNotification>();
            foreach (var s in list)
            {
                if (!Enum.TryParse<NotificationCategory>(s.Category, out var cat)) continue;
                var entry = new NotificationEntry(cat, s.Id, s.Title, s.Description, s.CreatedAtUtc);
                _entries[(entry.Category, entry.Id)] = entry;
            }
            TrimIfNecessary_NoLock();
        }
        catch
        {
            // ignore load errors, start empty
        }
    }

    private void Persist_NoLock()
    {
        try
        {
            var stored = _entries.Values
                .OrderBy(e => e.CreatedAt)
                .Select(e => new StoredNotification
                {
                    Category = e.Category.ToString(),
                    Id = e.Id,
                    Title = e.Title,
                    Description = e.Description,
                    CreatedAtUtc = e.CreatedAt
                })
                .ToList();
            _configService.Current.Notifications = stored;
            _configService.Save();
        }
        catch
        {
            // ignore persistence errors
        }
    }

    private void TrimIfNecessary_NoLock()
    {
        if (_entries.Count <= MaxStored) return;
        foreach (var kv in _entries.Values.OrderByDescending(v => v.CreatedAt).Skip(MaxStored).ToList())
        {
            _entries.Remove((kv.Category, kv.Id));
        }
    }
}
