using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class KnowledgeGroupEditViewModel : ObservableObject
{
    public long ID { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Active { get; set; } = true;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    public ObservableCollection<KnowledgeEntryEditViewModel> Entries { get; } = [];
}
