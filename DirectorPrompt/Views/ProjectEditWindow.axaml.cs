using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using DirectorPrompt.Localization;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;
using FluentAvalonia.UI.Windowing;

namespace DirectorPrompt.Views;

public partial class ProjectEditWindow : FAAppWindow, IRemoteDialogOwner
{
    private Action<bool>? remoteCloseAction;

    public ProjectEditViewModel ViewModel { get; }

    public IRemoteDialogHost? RemoteDialogHost { get; set; }

    public ProjectEditWindow()
    {
        ViewModel = null!;
        AvaloniaXamlLoader.Load(this);
    }

    public ProjectEditWindow(ProjectEditViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
    }

    internal void SetRemoteCloseAction(Action<bool>? action) =>
        remoteCloseAction = action;

    private void Complete(bool result)
    {
        if (remoteCloseAction is { } action)
        {
            remoteCloseAction = null;
            action(result);
            return;
        }

        Close(result);
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: ListBoxItem item })
            return;

        var tag    = item.Tag as string;
        if (sender is not Visual visual)
            return;

        var root = TopLevel.GetTopLevel(visual);

        if (root is null)
            return;

        var panels = root.GetVisualDescendants().OfType<StackPanel>().Where(panel => panel.Name is not null);

        foreach (var panel in panels)
        {
            panel.IsVisible = panel.Name switch
            {
                "BasicPanel"     => tag == "basic",
                "KnowledgePanel" => tag == "knowledge",
                "StatePanel"     => tag == "state",
                "CharacterPanel" => tag == "character",
                _                => panel.IsVisible
            };
        }
    }

    private async void OnDeleteKnowledgeGroup(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: KnowledgeGroupEditViewModel group })
            return;

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Delete"), Loc.Get("Dialog.ConfirmDeleteKnowledgeGroup", group.Name), true))
            return;

        ViewModel.DeleteKnowledgeGroupCommand.Execute(group);
    }

    private void OnEditKnowledgeEntry(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: KnowledgeEntryEditViewModel entry })
            entry.IsEditing = !entry.IsEditing;
    }

    private async void OnDeleteKnowledgeEntry(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: KnowledgeEntryEditViewModel entry })
            return;

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Delete"), Loc.Get("Dialog.ConfirmDeleteKnowledgeEntry", entry.Remarks), true))
            return;

        ViewModel.DeleteKnowledgeEntryCommand.Execute(entry);
    }

    private void OnEditStateAttribute(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: StateAttributeEditViewModel attr })
            attr.IsEditing = !attr.IsEditing;
    }

    private async void OnDeleteStateAttribute(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: StateAttributeEditViewModel attr })
            return;

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Delete"), Loc.Get("Dialog.ConfirmDeleteStateAttribute", attr.DisplayName), true))
            return;

        ViewModel.DeleteStateAttributeCommand.Execute(attr);
    }

    private void OnEditCharacterCategory(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: CharacterCategoryEditViewModel })
        {
            if (sender is Button btn)
            {
                var expander = btn.GetVisualAncestors().OfType<Expander>().FirstOrDefault();
                if (expander is not null)
                    expander.IsExpanded = !expander.IsExpanded;
            }
        }
    }

    private async void OnDeleteCharacterCategory(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: CharacterCategoryEditViewModel category })
            return;

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Delete"), Loc.Get("Dialog.ConfirmDeleteCharacterCategory", category.Name), true))
            return;

        ViewModel.DeleteCharacterCategoryCommand.Execute(category);
    }

    private void OnAddCategoryStateAttribute(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: CharacterCategoryEditViewModel category })
            ViewModel.AddCategoryStateAttributeCommand.Execute(category);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);

        if (ViewModel.SaveSuccess)
            Complete(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) =>
        Complete(false);
}
