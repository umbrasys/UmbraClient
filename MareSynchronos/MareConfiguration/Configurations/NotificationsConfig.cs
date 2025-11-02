using System;
using System.Collections.Generic;
using MareSynchronos.MareConfiguration.Models;

namespace MareSynchronos.MareConfiguration.Configurations;

[Serializable]
public class NotificationsConfig : IMareConfiguration
{
    public List<StoredNotification> Notifications { get; set; } = new();
    public int Version { get; set; } = 1;
}
