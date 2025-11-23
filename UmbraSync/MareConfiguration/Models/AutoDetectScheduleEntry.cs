namespace UmbraSync.MareConfiguration.Models;

[Serializable]
public class AutoDetectScheduleEntry
{
    public bool Recurring { get; set; }
    public int DurationHours { get; set; } = 2;
    public bool[] Weekdays { get; set; } = new bool[7];
    public int StartHour { get; set; }
    public int StartMinute { get; set; }
    public int EndHour { get; set; }
    public int EndMinute { get; set; }
    public string TimeZone { get; set; } = "Europe/Paris";
    public int Version { get; set; } = 0;
}
