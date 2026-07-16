using System.Text.Json.Serialization;

namespace DirectorPrompt.Agents;

public sealed class DirectiveEventData
{
    public string Type { get; set; } = "Plot";

    public string Content { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    [JsonPropertyName("ttl")]
    public int? TTL { get; set; }

    public int Order { get; set; }
}
