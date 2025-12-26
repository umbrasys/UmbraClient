using UmbraSync.MareConfiguration.Configurations;

namespace UmbraSync.MareConfiguration;

public class RpConfigService : ConfigurationServiceBase<RpConfig>
{
    public const string ConfigName = "rp_profile.json";

    public RpConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}
