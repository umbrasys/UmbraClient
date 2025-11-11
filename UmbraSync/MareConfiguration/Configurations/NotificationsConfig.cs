using System;
using System.Collections.Generic;
using UmbraSync.MareConfiguration.Models;

namespace UmbraSync.MareConfiguration.Configurations;

[Serializable]
public class NotificationsConfig : IMareConfiguration
{
    public List<StoredNotification> Notifications { get; set; } = new();
    public int Version { get; set; } = 1;
}
