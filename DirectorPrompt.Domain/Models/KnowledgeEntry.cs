namespace DirectorPrompt.Domain.Models;

public record KnowledgeEntry
{
    public long ID { get; init; }

    public long ProjectID { get; init; }

    public string Remarks { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string[] Keywords { get; init; } = [];

    public long? GroupID { get; init; }

    public bool Active { get; init; } = true;

    public string? ContentHash { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}
