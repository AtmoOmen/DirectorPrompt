using System.ComponentModel;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed class PipelineStageViewModel : INotifyPropertyChanged
{
    private PipelineStageStatus status;

    public PipelineStageKind Kind { get; init; }

    public string Stage => Loc.Get
    (
        Kind switch
        {
            PipelineStageKind.DirectiveProcessing => "Pipeline.Stage.DirectiveProcessing",
            PipelineStageKind.Retrieval           => "Pipeline.Stage.Retrieval",
            PipelineStageKind.Generation          => "Pipeline.Stage.Generation",
            PipelineStageKind.PostProcessing      => "Pipeline.Stage.PostProcessing",
            _                                     => Kind.ToString()
        }
    );

    public PipelineStageStatus Status
    {
        get => status;
        set
        {
            if (status != value)
            {
                status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }
    }

    public string StatusText => Loc.Get
    (
        status switch
        {
            PipelineStageStatus.Running  => "Pipeline.Status.Running",
            PipelineStageStatus.Complete => "Pipeline.Status.Complete",
            PipelineStageStatus.Failed   => "Pipeline.Status.Failed",
            _                            => status.ToString()
        }
    );

    public string? Detail
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
