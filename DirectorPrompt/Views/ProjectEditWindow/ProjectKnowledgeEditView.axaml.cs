using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views;

public partial class ProjectKnowledgeEditView : UserControl
{
    public ProjectEditViewModel? ViewModel => DataContext as ProjectEditViewModel;

    public ProjectKnowledgeEditView() =>
        AvaloniaXamlLoader.Load(this);

    private async void OnDeleteKnowledgeGroup(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: KnowledgeGroupEditViewModel group } || ViewModel is null)
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var confirmed = await PromptDialog.ConfirmAsync
                        (
                            owner,
                            Loc.Get("Common.Remove"),
                            Loc.Get("Dialog.ConfirmDeleteKnowledgeGroup", group.Name),
                            true
                        );

        if (confirmed)
            ViewModel.DeleteKnowledgeGroupCommand.Execute(group);
    }

    private async void OnDeleteKnowledgeEntry(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: KnowledgeEntryEditViewModel entry } || ViewModel is null)
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var confirmed = await PromptDialog.ConfirmAsync
                        (
                            owner,
                            Loc.Get("Common.Remove"),
                            Loc.Get("Dialog.ConfirmDeleteKnowledgeEntry", entry.Remarks),
                            true
                        );

        if (confirmed)
            ViewModel.DeleteKnowledgeEntryCommand.Execute(entry);
    }
}
