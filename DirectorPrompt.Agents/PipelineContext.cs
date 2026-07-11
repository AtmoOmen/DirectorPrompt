using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Agents;

public sealed class PipelineContext
{
    public required DirectiveBatch DirectiveBatch { get; init; }

    public required long RoundID { get; init; }

    public long SessionID { get; init; }

    public long? CurrentSceneID { get; init; }

    public long CurrentTimelinePosition { get; init; }

    public Project? Project { get; set; }

    public IReadOnlyList<ChatHistoryEntry> History { get; set; } = [];

    public string? KnowledgeContext { get; set; }

    public string? MemoryContext { get; set; }

    public string? SystemInjection { get; set; }

    public string? NarrativeOutput { get; set; }

    public string? ThinkingOutput { get; set; }

    public string? PreviousSceneSummary { get; set; }

    public Action<string, string>? OnStreamingUpdate { get; set; }

    public Action<PipelineStageUpdate>? OnStageUpdate { get; set; }

    public ToolExecutionContext ToolContext => new
    (
        DirectiveBatch.ProjectID,
        SessionID,
        CurrentSceneID,
        CurrentTimelinePosition,
        RoundID,
        EmbeddingConfig,
        KnowledgeConfig,
        MemoryConfig,
        PhaseActivatedEntryIDs
    );

    public required ResolvedEmbeddingConfig EmbeddingConfig { get; init; }

    public required KnowledgeRetrievalConfig KnowledgeConfig { get; init; }

    public required MemoryConfig MemoryConfig { get; init; }

    public IReadOnlyList<long>? PhaseActivatedEntryIDs { get; set; }
}

public record ChatHistoryEntry
(
    long   RoundID,
    string DirectorInput,
    string NarrativeOutput
);

public record PipelineStageUpdate
(
    PipelineStageKind   Stage,
    PipelineStageStatus Status,
    string?             Detail = null
);
