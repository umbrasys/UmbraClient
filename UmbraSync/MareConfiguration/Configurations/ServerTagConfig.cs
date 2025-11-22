using UmbraSync.MareConfiguration.Models;

namespace UmbraSync.MareConfiguration.Configurations;

[Serializable]
public class ServerTagConfig : IMareConfiguration
{
    public Dictionary<string, ServerTagStorage> ServerTagStorage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 0;
}