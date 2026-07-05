using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class KnowledgeEntryEditViewModel : ObservableObject
{
    public long ID { get; set; }

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string content = string.Empty;

    [ObservableProperty]
    private string tags = string.Empty;

    [ObservableProperty]
    private long? groupID;

    [ObservableProperty]
    private bool active = true;

    [ObservableProperty]
    private bool isEditing;

    public string GroupDisplay { get; set; } = string.Empty;
}
