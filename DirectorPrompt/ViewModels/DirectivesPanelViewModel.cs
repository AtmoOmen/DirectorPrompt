using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed partial class DirectivePanelItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial long ID { get; set; }

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TTLLabel))]
    public partial int? TTL { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditingContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int? EditingTTL { get; set; }

    [ObservableProperty]
    public partial bool EditingIsPermanent { get; set; }

    public string TTLLabel => TTL.HasValue ?
                                  Loc.Get("Directive.Panel.RemainingRounds", TTL) :
                                  Loc.Get("Directive.Permanent");

    public void StartEdit()
    {
        EditingContent     = Content;
        EditingTTL         = TTL;
        EditingIsPermanent = !TTL.HasValue;
        IsEditing          = true;
    }

    public void CancelEdit() =>
        IsEditing = false;

    public void CommitEdit()
    {
        Content   = EditingContent.Trim();
        TTL       = EditingIsPermanent ? null : EditingTTL ?? 5;
        IsEditing = false;
    }

    partial void OnEditingIsPermanentChanged(bool value)
    {
        if (!value && EditingTTL is null)
            EditingTTL = 5;
    }
}

public sealed class DirectivesPanelViewModel : ObservableObject
{
    public ObservableCollection<DirectivePanelItemViewModel> Directives { get; } = [];

    public void Clear() =>
        Directives.Clear();

    public void RemoveItem(DirectivePanelItemViewModel item) =>
        Directives.Remove(item);
}
