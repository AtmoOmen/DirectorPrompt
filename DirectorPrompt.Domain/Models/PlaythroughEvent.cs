using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Models;

public record PlaythroughEvent
{
    public long ID { get; init; }

    public long ProjectID { get; init; }

    public long SessionID { get; init; }

    public long RoundID { get; init; }

    public long? SceneID { get; init; }

    public EventType Type { get; init; }

    /// <summary>
    ///     JSON 格式的事件数据, 结构由 EventType 决定
    /// </summary>
    public string Data { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }
}
