using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Services;

public record TransitionResult
{
    public required IReadOnlyList<DirectiveConfig> EnterDirectives { get; init; } = [];

    public required IReadOnlyList<DirectiveConfig> ExitDirectives { get; init; } = [];

    public required IReadOnlyList<string> ActiveKeys { get; init; } = [];
}
