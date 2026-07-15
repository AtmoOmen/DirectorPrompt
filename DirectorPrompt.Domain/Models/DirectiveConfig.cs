using System.Text.Json.Serialization;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Models;

public record DirectiveConfig
{
    public DirectiveType Type { get; init; } = DirectiveType.Plot;

    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("ttl")]
    public int? TTL { get; init; }
}
