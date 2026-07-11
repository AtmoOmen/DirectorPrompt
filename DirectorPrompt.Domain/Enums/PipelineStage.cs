namespace DirectorPrompt.Domain.Enums;

public enum PipelineStageKind
{
    DirectiveProcessing,
    Retrieval,
    Generation,
    PostProcessing,
    SystemState
}

public enum PipelineStageStatus
{
    Running,
    Complete,
    Failed
}
