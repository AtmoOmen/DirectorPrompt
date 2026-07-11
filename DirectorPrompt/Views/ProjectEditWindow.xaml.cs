using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using ListViewItem = System.Windows.Controls.ListViewItem;

namespace DirectorPrompt.Views;

public partial class ProjectEditWindow : FluentWindow
{
    public ProjectEditViewModel ViewModel { get; }

    public ProjectEditWindow(ProjectEditViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BasicPanel is null)
            return;

        if (NavList.SelectedItem is not ListViewItem item)
            return;

        var tag = item.Tag as string;

        BasicPanel.Visibility = tag == "basic" ?
                                    Visibility.Visible :
                                    Visibility.Collapsed;
        KnowledgePanel.Visibility = tag == "knowledge" ?
                                        Visibility.Visible :
                                        Visibility.Collapsed;
        StatePanel.Visibility = tag == "state" ?
                                    Visibility.Visible :
                                    Visibility.Collapsed;
        CharacterPanel.Visibility = tag == "character" ?
                                        Visibility.Visible :
                                        Visibility.Collapsed;
        MemoryPanel.Visibility = tag == "memory" ?
                                     Visibility.Visible :
                                     Visibility.Collapsed;
        RetrievalPanel.Visibility = tag == "retrieval" ?
                                        Visibility.Visible :
                                        Visibility.Collapsed;
    }

    private void OnDeleteKnowledgeGroup(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: KnowledgeGroupEditViewModel group })
            return;

        if (!PromptDialog.Confirm(this, Loc.Get("Common.Delete"), Loc.Get("Dialog.ConfirmDeleteKnowledgeGroup", group.Name), true))
            return;

        ViewModel.DeleteKnowledgeGroupCommand.Execute(group);
    }

    private void OnEditKnowledgeEntry(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: KnowledgeEntryEditViewModel entry })
            entry.IsEditing = !entry.IsEditing;
    }

    private void OnDeleteKnowledgeEntry(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: KnowledgeEntryEditViewModel entry })
            return;

        if (!PromptDialog.Confirm(this, Loc.Get("Common.Delete"), Loc.Get("Dialog.ConfirmDeleteKnowledgeEntry", entry.Remarks), true))
            return;

        ViewModel.DeleteKnowledgeEntryCommand.Execute(entry);
    }

    private void OnEditStateAttribute(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StateAttributeEditViewModel attr })
            attr.IsEditing = !attr.IsEditing;
    }

    private void OnDeleteStateAttribute(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: StateAttributeEditViewModel attr })
            return;

        if (!PromptDialog.Confirm(this, Loc.Get("Common.Delete"), Loc.Get("Dialog.ConfirmDeleteStateAttribute", attr.DisplayName), true))
            return;

        ViewModel.DeleteStateAttributeCommand.Execute(attr);
    }

    private void OnEditCharacterCategory(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CharacterCategoryEditViewModel })
        {
            if (sender is Button btn)
            {
                var expander = FindAncestor<CardExpander>(btn);
                if (expander is not null)
                    expander.IsExpanded = !expander.IsExpanded;
            }
        }
    }

    private void OnDeleteCharacterCategory(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CharacterCategoryEditViewModel category })
            return;

        if (!PromptDialog.Confirm(this, Loc.Get("Common.Delete"), Loc.Get("Dialog.ConfirmDeleteCharacterCategory", category.Name), true))
            return;

        ViewModel.DeleteCharacterCategoryCommand.Execute(category);
    }

    private void OnAddCategoryStateAttribute(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CharacterCategoryEditViewModel category })
            ViewModel.AddCategoryStateAttributeCommand.Execute(category);
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T target)
                return target;

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);

        if (ViewModel.SaveSuccess)
        {
            DialogResult = true;
            Close();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
