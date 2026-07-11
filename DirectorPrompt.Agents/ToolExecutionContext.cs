using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.Agents;

public record ToolExecutionContext
(
    long                    ProjectID,
    long                    SessionID,
    long?                   SceneID,
    long                    TimelinePosition,
    long                    RoundID,
    ResolvedEmbeddingConfig EmbeddingConfig,
    KnowledgeRetrievalConfig KnowledgeConfig,
    MemoryConfig             MemoryConfig,
    IReadOnlyList<long>?    PhaseActivatedEntryIDs = null
);
