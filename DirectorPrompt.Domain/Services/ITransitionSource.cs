using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Services;

public record TransitionResult
{
    public required IReadOnlyList<DirectiveConfig> EnterDirectives { get; init; } = [];

    public required IReadOnlyList<DirectiveConfig> ExitDirectives { get; init; } = [];

    public required IReadOnlyList<string> ActiveKeys { get; init; } = [];
}

public interface ITransitionSource
{
    string SourceName { get; }

    EventType EventType { get; }

    Task<TransitionResult> EvaluateAsync
    (
        long                   projectID,
        long                   sessionID,
        IReadOnlyList<string>? previousKeys,
        CancellationToken      cancellationToken = default
    );
}
