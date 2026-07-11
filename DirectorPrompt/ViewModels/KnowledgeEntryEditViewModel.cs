using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class KnowledgeEntryEditViewModel : ObservableObject
{
    public long ID { get; set; }

    [ObservableProperty]
    public partial string Remarks { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Keywords { get; set; } = string.Empty;

    [ObservableProperty]
    public partial long? GroupID { get; set; }

    [ObservableProperty]
    public partial bool Active { get; set; } = true;

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    public string GroupDisplay { get; set; } = string.Empty;
}
