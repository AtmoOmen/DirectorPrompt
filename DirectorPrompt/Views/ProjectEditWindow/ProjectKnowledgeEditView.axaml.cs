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

        var message = Loc.Get("Dialog.ConfirmDeleteKnowledgeGroup", group.Name);

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), message, true))
            return;

        ViewModel.DeleteKnowledgeGroupCommand.Execute(group);
    }

    private async void OnDeleteKnowledgeEntry(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: KnowledgeEntryEditViewModel entry } || ViewModel is null)
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteKnowledgeEntry", entry.Remarks);

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), message, true))
            return;

        ViewModel.DeleteKnowledgeEntryCommand.Execute(entry);
    }

    private void OnAddKnowledgeEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: KnowledgeGroupEditViewModel group } || ViewModel is null)
            return;

        ViewModel.AddKnowledgeEntryCommand.Execute(group);
    }
}
