using UmbraSync.MareConfiguration.Configurations;

namespace UmbraSync.MareConfiguration;

public class ServerBlockConfigService : ConfigurationServiceBase<ServerBlockConfig>
{
    public const string ConfigName = "blocks.json";

    public ServerBlockConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}