namespace DirectorPrompt.Domain.Models;

public record Project
{
    public long ID { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string OpeningMessage { get; init; } = string.Empty;

    public string AuditConfig { get; init; } = "{}";

    public string MemoryConfig { get; init; } = "{}";

    public string KnowledgeConfig { get; init; } = "{}";

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}
