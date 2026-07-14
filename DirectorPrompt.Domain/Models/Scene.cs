using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Models;

public record Scene
{
    public long ID { get; init; }

    public long ProjectID { get; init; }

    public long SessionID { get; init; }

    public long TimelinePosition { get; init; }

    public string TimeLabel { get; init; } = string.Empty;

    public string? Summary { get; init; }

    public string? ProgressSummary { get; init; }

    public long ProgressSummaryRoundID { get; init; }

    public SceneStatus Status { get; init; }
}
