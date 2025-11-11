using System;
using System.Collections.Generic;

namespace MareSynchronos.MareConfiguration.Models;

[Serializable]
public class StoredNotification
{
    public string Category { get; set; } = string.Empty; // name of enum NotificationCategory
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
