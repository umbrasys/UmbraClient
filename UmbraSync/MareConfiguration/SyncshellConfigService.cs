using UmbraSync.MareConfiguration.Configurations;

namespace UmbraSync.MareConfiguration;

public class SyncshellConfigService(string configDir) : ConfigurationServiceBase<SyncshellConfig>(configDir)
{
    public const string ConfigName = "syncshells.json";

    public override string ConfigurationName => ConfigName;
}