namespace UmbraSync.PlayerData.Pairs;

public record OptionalPluginWarning
{
    public bool ShownHeelsWarning { get; set; }
    public bool ShownCustomizePlusWarning { get; set; }
    public bool ShownHonorificWarning { get; set; }
    public bool ShowPetNicknamesWarning { get; set; }
    public bool ShownMoodlesWarning { get; set; }
}