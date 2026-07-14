namespace DirectorPrompt.Domain.Models;

public sealed record MemoryPage
(
    IReadOnlyList<MemoryEntry> Items,
    long?                      NextTimelinePosition,
    long?                      NextID
);
