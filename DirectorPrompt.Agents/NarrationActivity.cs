using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents;

public sealed record NarrationActivity
(
    int                                  ID,
    int                                  Turn,
    NarrationActivityKind                Kind,
    string                               Title,
    string                               Content,
    PipelineStageStatus                  Status,
    string?                              Arguments     = null,
    IReadOnlyList<NarrationRecallEntry>? RecallEntries = null
);
