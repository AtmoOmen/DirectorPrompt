namespace DirectorPrompt.Domain.Models;

public sealed record MemoryPageQuery
(
    long    SessionID,
    long    MaxTimelinePosition,
    long?   BeforeTimelinePosition = null,
    long?   BeforeID               = null,
    long?   SceneID                = null,
    string? Tag                    = null,
    string? SearchText             = null,
    int     PageSize               = 100
);
