using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed class NarrationActivityViewModel
(
    NarrationActivity activity
) : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions DisplayOptions = new()
    {
        Encoder       = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public NarrationActivity Activity { get; private set; } = activity;

    public string Content { get; private set; } = activity.Kind == NarrationActivityKind.Tool ?
                                                      FormatJSON(activity.Content) :
                                                      activity.Content;

    public string? Arguments { get; private set; } = activity.Arguments is { } arguments ?
                                                         FormatJSON(arguments) :
                                                         null;

    public bool HasArguments => Activity.Arguments is not null;

    public IReadOnlyList<NarrationRecallEntry> RecallEntries => Activity.RecallEntries ?? [];

    public bool HasRecallEntries => RecallEntries.Count > 0;

    public string Header => Activity.Kind == NarrationActivityKind.Retrieval ?
                                Loc.Get(Activity.Title) :
                                Loc.Get
                                (
                                    "Dialog.AgentActivity",
                                    Activity.Turn,
                                    Activity.Title,
                                    Loc.Get
                                    (
                                        Activity.Status switch
                                        {
                                            PipelineStageStatus.Running  => "Pipeline.Status.Running",
                                            PipelineStageStatus.Complete => "Pipeline.Status.Complete",
                                            _                            => "Pipeline.Status.Failed"
                                        }
                                    )
                                );

    public bool IsRunning => Activity.Status == PipelineStageStatus.Running;

    public bool HasError => Activity.Status == PipelineStageStatus.Failed;

    public bool IsExpanded
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public void Update(NarrationActivity value)
    {
        var previous = Activity;
        Activity = value;

        if (!ReferenceEquals(previous.RecallEntries, value.RecallEntries))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecallEntries)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRecallEntries)));
        }

        if (previous.Content != value.Content)
        {
            Content = value.Kind == NarrationActivityKind.Tool ?
                          FormatJSON(value.Content) :
                          value.Content;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
        }

        if (previous.Arguments != value.Arguments)
        {
            Arguments = value.Arguments is { } arguments ?
                            FormatJSON(arguments) :
                            null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Arguments)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasArguments)));
        }

        if (previous.Status != value.Status)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
        }

        if (previous.Status != value.Status || previous.Turn != value.Turn || previous.Title != value.Title)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Header)));
    }

    private static string FormatJSON(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        try
        {
            using var document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(document.RootElement, DisplayOptions);
        }
        catch (JsonException)
        {
            return value;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
