using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents;

public record PipelineStageUpdate
(
    PipelineStageKind   Stage,
    PipelineStageStatus Status,
    string?             Detail = null
);
