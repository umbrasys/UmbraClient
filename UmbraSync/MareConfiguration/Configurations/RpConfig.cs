using UmbraSync.MareConfiguration.Configurations;

namespace UmbraSync.MareConfiguration.Configurations;

[Serializable]
public class RpConfig : IMareConfiguration
{
    public string RpDescription { get; set; } = string.Empty;
    public string RpProfilePictureBase64 { get; set; } = string.Empty;
    public bool IsRpNsfw { get; set; } = false;
    public int Version { get; set; } = 1;
}
