using System.Text.Json.Serialization;

namespace MareSynchronos.Services;

public record RepoChangeConfig
{
    [JsonPropertyName("current_repo")]
    public string? CurrentRepo { get; set; }

    [JsonPropertyName("valid_repos")]
    public string[]? ValidRepos { get; set; }
}