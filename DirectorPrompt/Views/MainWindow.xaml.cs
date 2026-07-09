﻿using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
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

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        WindowTitleBar.Title = $"DirectorPrompt {version}";

        viewModel.Dialog.Entries.CollectionChanged += OnDialogEntriesChanged;
        Loaded                                     += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await viewModel.LoadProjectsCommand.ExecuteAsync(null);

    private void OnDialogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            return;

        ScrollDialogToBottom();
    }

    private void ScrollDialogToBottom() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, DialogScrollViewer.ScrollToBottom);

    private void OnRollbackRound(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel })
            _ = viewModel.RollbackLastRoundCommand.ExecuteAsync(null);
    }

    private void OnRewriteRound(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel })
            _ = viewModel.RewriteLastRoundCommand.ExecuteAsync(null);
    }

    private void OnCorrectRound(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel })
            _ = viewModel.CorrectLastRoundCommand.ExecuteAsync(null);
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

    private void OnExportProjectItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Project project })
            return;

        viewModel.CurrentProject = project;
        viewModel.ExportProjectCommand.Execute(null);
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

    private void OnEditMemory(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MemoryPanelItemViewModel item })
            item.StartEdit();
    }

    private void OnDeleteMemory(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MemoryPanelItemViewModel item })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteMemory");

        if (PromptDialog.Confirm(this, Loc.Get("Common.Delete"), message, true))
            _ = viewModel.DeleteMemoryCommand.ExecuteAsync(item);
    }
}
