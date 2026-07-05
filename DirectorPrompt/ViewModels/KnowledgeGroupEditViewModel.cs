using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class KnowledgeGroupEditViewModel : ObservableObject
{
    public long ID { get; set; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private bool active = true;

    [ObservableProperty]
    private bool isExpanded = true;

    public ObservableCollection<KnowledgeEntryEditViewModel> Entries { get; } = [];
}
