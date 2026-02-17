using System.Text.Json.Serialization;

namespace UmbraSync.Models;

public class MoodleFullStatus
{
    [JsonPropertyName("GUID")]
    public string GUID { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("IconID")]
    public int IconID { get; set; } = 210456;

    [JsonPropertyName("Title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("Description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("Type")]
    public int Type { get; set; }

    [JsonPropertyName("Stacks")]
    public int Stacks { get; set; } = 1;

    [JsonPropertyName("StackSteps")]
    public int StackSteps { get; set; } = 0;

    [JsonPropertyName("Modifiers")]
    public int Modifiers { get; set; }

    [JsonPropertyName("CustomFXPath")]
    public string CustomFXPath { get; set; } = string.Empty;

    [JsonPropertyName("Applier")]
    public string Applier { get; set; } = string.Empty;

    [JsonPropertyName("Dispeller")]
    public string Dispeller { get; set; } = string.Empty;

    [JsonPropertyName("ChainedStatus")]
    public string ChainedStatus { get; set; } = "00000000-0000-0000-0000-000000000000";

    [JsonPropertyName("ChainTrigger")]
    public int ChainTrigger { get; set; }

    [JsonPropertyName("Persistent")]
    public bool Persistent { get; set; }

    [JsonPropertyName("NoExpire")]
    public bool NoExpire { get; set; } = true;

    [JsonPropertyName("AsPermanent")]
    public bool AsPermanent { get; set; }

    [JsonPropertyName("Days")]
    public int Days { get; set; }

    [JsonPropertyName("Hours")]
    public int Hours { get; set; }

    [JsonPropertyName("Minutes")]
    public int Minutes { get; set; }

    [JsonPropertyName("Seconds")]
    public int Seconds { get; set; }

    [JsonPropertyName("ExpiresAt")]
    public long ExpiresAt { get; set; } = long.MaxValue;
}
