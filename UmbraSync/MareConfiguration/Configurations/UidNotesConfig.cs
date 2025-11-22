using UmbraSync.MareConfiguration.Models;

namespace UmbraSync.MareConfiguration.Configurations;

[Serializable]
public class UidNotesConfig : IMareConfiguration
{
    public Dictionary<string, ServerNotesStorage> ServerNotes { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}
