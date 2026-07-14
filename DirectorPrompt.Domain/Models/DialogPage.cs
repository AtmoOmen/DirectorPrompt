namespace DirectorPrompt.Domain.Models;

public sealed record DialogPage
(
    IReadOnlyList<PlaythroughEvent> Events,
    long?                           PreviousRoundID
);
