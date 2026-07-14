namespace DirectorPrompt.Domain.Models;

public sealed record CharacterPageQuery
(
    long    SessionID,
    long?   AfterID    = null,
    long?   CategoryID = null,
    string? SearchText = null,
    int     PageSize   = 100
);
