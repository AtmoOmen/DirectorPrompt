namespace DirectorPrompt.Domain.Enums;

public enum PipelineStageKind
{
    DirectiveProcessing,
    Retrieval,
    Generation,
    Audit,
    PostProcessing,
    SystemState
}

public enum PipelineStageStatus
{
    Running,
    Complete
}
