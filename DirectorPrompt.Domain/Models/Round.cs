using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Models;

public record Round
{
    public long ID { get; init; }

    public long ProjectID { get; init; }

    public long SessionID { get; init; }

    public long SceneID { get; init; }

    public RoundStatus Status { get; init; }

    public DateTime CreatedAt { get; init; }
}
