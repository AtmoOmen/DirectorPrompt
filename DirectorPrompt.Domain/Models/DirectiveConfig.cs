using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Models;

public record DirectiveConfig
{
    public DirectiveType Type { get; init; } = DirectiveType.Plot;

    public string Content { get; init; } = string.Empty;

    public int? TTL { get; init; }
}
