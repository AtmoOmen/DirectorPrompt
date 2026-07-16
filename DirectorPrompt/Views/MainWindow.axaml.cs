using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    private ScrollViewer? dialogScrollViewer;

    private ListBox DialogList =>
        this.GetLogicalDescendants().OfType<ListBox>().First(control => control.Name == "DialogListBox");

    public MainWindow()
    {
        viewModel = null!;
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext    = viewModel;
        AvaloniaXamlLoader.Load(this);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"DirectorPrompt {version}";

        viewModel.Dialog.Entries.CollectionChanged += OnDialogEntriesChanged;
        viewModel.PropertyChanged                  += OnViewModelPropertyChanged;
        Loaded                                     += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        dialogScrollViewer = DialogList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        await viewModel.LoadProjectsCommand.ExecuteAsync(null);
    }

    private void OnDialogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            return;

        if (viewModel.IsLoadingDialog)
            return;

        ScrollDialogToBottom();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsLoadingDialog) && !viewModel.IsLoadingDialog)
            ScrollDialogToBottom();
    }

    private void ScrollDialogToBottom() =>
        Dispatcher.UIThread.Post
        (
            () =>
            {
                if (dialogScrollViewer is not null)
                    dialogScrollViewer.Offset = dialogScrollViewer.Offset.WithY(double.MaxValue);
                else if (viewModel.Dialog.Entries.Count > 0)
                    DialogList.ScrollIntoView(viewModel.Dialog.Entries[^1]);
            },
            DispatcherPriority.Background
        );

    private void OnRollbackRound(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel })
            _ = viewModel.RollbackLastRoundCommand.ExecuteAsync(null);
    }

    private async void OnCopyEntry(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel entry })
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

            if (clipboard is not null)
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateText(entry.Content));
                await clipboard.SetDataAsync(transfer);
            }
        }
    }

    private void OnEditEntry(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel entry })
            entry.StartEdit();
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { ContextMenu: { } menu } element)
            return;

        menu.DataContext = element.DataContext;

        foreach (var item in menu.Items.OfType<MenuItem>())
            item.Tag = element.DataContext;

        menu.Open(element);
        e.Handled = true;
    }

    private void OnImportButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { ContextMenu: { } menu } element)
            return;

        menu.Open(element);
        e.Handled = true;
    }

    private void OnImportDirectorPrompt(object sender, RoutedEventArgs e) =>
        viewModel.ImportProjectCommand.Execute(null);

    private void OnImportSillyTavern(object sender, RoutedEventArgs e) =>
        viewModel.ImportSillyTavernProjectCommand.Execute(null);

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

    private async void OnDeleteProjectItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Project project })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteProject", project.Name);

        if (await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Delete"), message, true))
            _ = viewModel.DeleteProjectCommand.ExecuteAsync(project);
    }

    private async void OnRenameSessionItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Session session })
            return;

        var newTitle = await PromptDialog.InputAsync
        (
            this,
            Loc.Get("Dialog.RenameSessionTitle"),
            Loc.Get("Dialog.RenameSessionPrompt"),
            session.Title
        );

        if (newTitle is not null)
            await viewModel.RenameSessionAsync(session, newTitle);
    }

    private async void OnDeleteSessionItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Session session })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteSession", session.Title);

        if (await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Delete"), message, true))
            _ = viewModel.DeleteSessionCommand.ExecuteAsync(session);
    }

    private void OnEditMemory(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: MemoryPanelItemViewModel item })
            item.StartEdit();
    }

    private async void OnDeleteMemory(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: MemoryPanelItemViewModel item })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteMemory");

        if (await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Delete"), message, true))
            _ = viewModel.DeleteMemoryCommand.ExecuteAsync(item);
    }

}
