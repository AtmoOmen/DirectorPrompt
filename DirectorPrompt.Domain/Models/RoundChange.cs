namespace DirectorPrompt.Domain.Models;

public record RoundChange
{
    public long ID { get; init; }

    public long RoundID { get; init; }

    public string TableName { get; init; } = string.Empty;

    public long RecordID { get; init; }

    public string Operation { get; init; } = string.Empty;

    public string? OldData { get; init; }

    public DateTime CreatedAt { get; init; }
}
