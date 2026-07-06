using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace DirectorPrompt.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext    = viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await viewModel.LoadProjectsCommand.ExecuteAsync(null);

    private void OnDirectiveTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || viewModel is null)
            return;

        var index = comboBox.SelectedIndex;

        viewModel.DirectiveInput.SelectedType = index switch
        {
            0 => DirectiveType.Plot,
            1 => DirectiveType.Tone,
            2 => DirectiveType.TemporaryConstraint,
            3 => DirectiveType.SceneChange,
            _ => DirectiveType.Plot
        };
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(viewModel.DirectiveInput.InputContent))
        {
            viewModel.DirectiveInput.AddDirectiveCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDeleteRound(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel })
            _ = viewModel.DeleteLastRoundCommand.ExecuteAsync(null);
    }

    private void OnRewriteRound(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel })
            _ = viewModel.RewriteLastRoundCommand.ExecuteAsync(null);
    }

    private void OnEditEntry(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel entry })
            entry.StartEdit();
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu is not null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement       = PlacementMode.Bottom;
            element.ContextMenu.IsOpen          = true;
        }
    }

    private void OnEditProjectItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Project project })
            return;

        viewModel.CurrentProject = project;
        viewModel.EditProjectCommand.Execute(null);
    }

    private void OnDeleteProjectItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Project project })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteProject", project.Name);

        if (PromptDialog.Confirm(this, Loc.Get("Common.Delete"), message, true))
            _ = viewModel.DeleteProjectCommand.ExecuteAsync(project);
    }

    private async void OnRenameSessionItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Session session })
            return;

        var newTitle = PromptDialog.Input
        (
            this,
            Loc.Get("Dialog.RenameSessionTitle"),
            Loc.Get("Dialog.RenameSessionPrompt"),
            session.Title
        );

        if (newTitle is not null)
            await viewModel.RenameSessionAsync(session, newTitle);
    }

    private void OnDeleteSessionItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Session session })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteSession", session.Title);

        if (PromptDialog.Confirm(this, Loc.Get("Common.Delete"), message, true))
            _ = viewModel.DeleteSessionCommand.ExecuteAsync(session);
    }

    private void OnFlowDocumentPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        DialogScrollViewer.ScrollToVerticalOffset(DialogScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
