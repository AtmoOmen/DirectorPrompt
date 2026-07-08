namespace DirectorPrompt.Domain.Models;

public record MemoryEntry
{
    public long ID { get; init; }

    public long ProjectID { get; init; }

    public long SessionID { get; init; }

    public long SceneID { get; init; }

    public long TimelinePos { get; init; }

    public string Content { get; init; } = string.Empty;

    public string[] Tags { get; init; } = [];

    public long[] RelatedCharacterIDs { get; init; } = [];

    public string? ContentHash { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}
