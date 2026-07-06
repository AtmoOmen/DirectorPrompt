namespace DirectorPrompt.Domain.Services;

public record ConditionContext
(
    IReadOnlyDictionary<string, string> StateValues
);
