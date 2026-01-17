using UmbraSync.MareConfiguration.Configurations;

namespace UmbraSync.MareConfiguration;

public class ServerBlockConfigService(string configDir) : ConfigurationServiceBase<ServerBlockConfig>(configDir)
{
    public const string ConfigName = "blocks.json";

    public override string ConfigurationName => ConfigName;
}