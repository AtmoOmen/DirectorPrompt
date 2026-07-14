namespace DirectorPrompt.Domain.Models;

public sealed record DialogPageQuery
(
    long  SessionID,
    long? BeforeRoundID = null,
    int   PageSize      = 40
);
