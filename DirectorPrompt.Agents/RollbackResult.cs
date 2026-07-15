namespace DirectorPrompt.Agents;

public sealed record RollbackResult
(
    long                         RoundID,
    IReadOnlyList<DirectiveItem> Directives
);
