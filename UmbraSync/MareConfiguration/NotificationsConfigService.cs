using UmbraSync.MareConfiguration.Configurations;

namespace UmbraSync.MareConfiguration;

public class NotificationsConfigService : ConfigurationServiceBase<NotificationsConfig>
{
    public const string ConfigName = "notifications.json";

    public NotificationsConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}