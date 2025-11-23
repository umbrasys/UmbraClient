using UmbraSync.MareConfiguration.Configurations;

namespace UmbraSync.MareConfiguration;

public class AutoDetectScheduleConfigService : ConfigurationServiceBase<AutoDetectScheduleConfig>
{
    public const string ConfigName = "autodetect_schedule.json";

    public AutoDetectScheduleConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}
