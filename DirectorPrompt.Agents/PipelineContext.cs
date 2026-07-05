using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Agents;

public sealed class PipelineContext
{
    public required DirectiveBatch DirectiveBatch { get; init; }

    public required long RoundID { get; init; }

    public long? CurrentSceneID { get; init; }

    public long CurrentTimelinePosition { get; init; }

    public string? KnowledgeContext { get; set; }

    public string? MemoryContext { get; set; }

    public string? SystemInjection { get; set; }

    public string? NarrativeOutput { get; set; }

    public string? ThinkingOutput { get; set; }

    public List<Violation> Violations { get; } = [];

    public bool AuditPassed { get; set; }

    public int AuditRetryCount { get; set; }

    /// <summary>
    /// 流式回调, 参数为 (narrativeText, thinkingText), 表示当前累计的叙事文本和思考文本
    /// </summary>
    public Action<string, string>? OnStreamingUpdate { get; set; }

    public ToolExecutionContext ToolContext => new
    (
        DirectiveBatch.ProjectID,
        CurrentSceneID,
        CurrentTimelinePosition,
        RoundID
    );
}
