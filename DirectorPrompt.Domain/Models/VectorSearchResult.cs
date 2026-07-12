namespace DirectorPrompt.Domain.Models;

public sealed record VectorSearchResult
(
    long   EntryID,
    string Source,
    float  Distance
);
