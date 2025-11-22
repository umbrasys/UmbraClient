using UmbraSync.MareConfiguration.Models;

namespace UmbraSync.MareConfiguration.Configurations;

[Serializable]
public class ServerBlockConfig : IMareConfiguration
{
    public Dictionary<string, ServerBlockStorage> ServerBlocks { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}