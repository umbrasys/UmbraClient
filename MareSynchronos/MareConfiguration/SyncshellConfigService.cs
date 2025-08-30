using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class SyncshellConfigService : ConfigurationServiceBase<SyncshellConfig>
{
    public const string ConfigName = "syncshells.json";

    public SyncshellConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}