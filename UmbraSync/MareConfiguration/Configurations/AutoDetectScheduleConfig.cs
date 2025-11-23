using UmbraSync.MareConfiguration.Models;

namespace UmbraSync.MareConfiguration.Configurations;

[Serializable]
public class AutoDetectScheduleConfig : IMareConfiguration
{
    public Dictionary<string, AutoDetectScheduleEntry> Schedules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 0;
}
