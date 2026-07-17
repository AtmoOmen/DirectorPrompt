using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Localization;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;
using DirectorPrompt.Views.Components;
using FluentAvalonia.UI.Windowing;
using Serilog;

namespace DirectorPrompt.Views;

public partial class MainWindow : FAAppWindow
{
    private readonly MainViewModel       viewModel;
    private readonly bool                isRemote;
    private readonly ILanSharingService? lanSharingService;

    private ScrollViewer? dialogScrollViewer;
    
    private int  dialogScrollRequestID;
    private bool isMobileRemote;
    private bool closeAuthorized;
    private bool closeInProgress;

    private Border? remoteImportMenu;
    private Border? remoteImportMenuBackdrop;

    private ListBox DialogList =>
        this.GetLogicalDescendants().OfType<ListBox>().First(control => control.Name == "DialogListBox");

    public MainWindow()
    {
        viewModel = null!;
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindow(MainViewModel viewModel)
        : this(viewModel, true)
    {
    }

    public MainWindow(MainViewModel viewModel, ILanSharingService lanSharingService)
        : this(viewModel, true)
    {
        this.lanSharingService =  lanSharingService;
        Closing                += OnClosing;
    }

    internal MainWindow(MainViewModel viewModel, bool attachWindowBehavior)
    {
        this.viewModel = viewModel;
        isRemote       = !attachWindowBehavior;
        DataContext    = viewModel;
        AvaloniaXamlLoader.Load(this);
        RootLayout                = this.FindControl<Grid>(nameof(RootLayout))!;
        MobileSessionsToggle      = this.FindControl<ToggleButton>(nameof(MobileSessionsToggle))!;
        MobileDetailsToggle       = this.FindControl<ToggleButton>(nameof(MobileDetailsToggle))!;
        MobileMoreActionsButton   = this.FindControl<ToggleButton>(nameof(MobileMoreActionsButton))!;
        remoteImportMenu          = this.FindControl<Border>(nameof(RemoteImportMenu));
        remoteImportMenuBackdrop  = this.FindControl<Border>(nameof(RemoteImportMenuBackdrop));

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"DirectorPrompt {version}";

        if (attachWindowBehavior)
        {
            viewModel.Dialog.Entries.CollectionChanged += OnDialogEntriesChanged;
            viewModel.PropertyChanged                  += OnViewModelPropertyChanged;
            Loaded                                     += OnLoaded;
        }
        else
            RootLayout.SizeChanged += OnRemoteRootSizeChanged;
    }

    private void OnRemoteRootSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!isRemote || e.NewSize.Width <= 0)
            return;

        var useMobileLayout = e.NewSize.Width < 720;

        if (useMobileLayout == isMobileRemote)
            return;

        isMobileRemote = useMobileLayout;

        if (useMobileLayout)
            RootLayout.Classes.Add("mobile");
        else
            RootLayout.Classes.Remove("mobile");

        ResetMobileNavigation();
    }

    private void OnMobileSessionsToggleClick(object? sender, RoutedEventArgs e)
    {
        if (!isMobileRemote)
            return;

        if (MobileSessionsToggle.IsChecked == true)
            MobileDetailsToggle.IsChecked = false;
    }

    private void OnMobileDetailsToggleClick(object? sender, RoutedEventArgs e)
    {
        if (!isMobileRemote)
            return;

        if (MobileDetailsToggle.IsChecked == true)
            MobileSessionsToggle.IsChecked = false;
    }

    private void OnMobileBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MobileMoreActionsButton.IsChecked = false;
    }

    private void OnMobileMenuItemClick(object? sender, RoutedEventArgs e)
    {
        MobileMoreActionsButton.IsChecked = false;
    }

    private void OnMobileImportDirectorPromptClick(object? sender, RoutedEventArgs e)
    {
        MobileMoreActionsButton.IsChecked = false;
        viewModel.ImportProjectCommand.Execute(null);
    }

    private void OnMobileImportSillyTavernClick(object? sender, RoutedEventArgs e)
    {
        MobileMoreActionsButton.IsChecked = false;
        viewModel.ImportSillyTavernProjectCommand.Execute(null);
    }

    private void OnSessionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isMobileRemote)
            ResetMobileNavigation();
    }

    private void ResetMobileNavigation()
    {
        MobileSessionsToggle.IsChecked    = false;
        MobileDetailsToggle.IsChecked     = false;
        MobileMoreActionsButton.IsChecked = false;
        remoteImportMenu!.IsVisible        = false;
        remoteImportMenuBackdrop!.IsVisible = false;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        dialogScrollViewer = DialogList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        await viewModel.LoadProjectsCommand.ExecuteAsync(null);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closeAuthorized)
            return;

        e.Cancel = true;

        if (closeInProgress || lanSharingService is null)
            return;

        closeInProgress = true;
        IsEnabled       = false;

        try
        {
            await lanSharingService.ApplyAsync(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "主窗口关闭前停止局域网共享失败");
        }
        finally
        {
            closeAuthorized = true;
            Dispatcher.UIThread.Post(Close, DispatcherPriority.Send);
        }
    }

    private void OnDialogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            return;

        if (viewModel.IsLoadingDialog || viewModel.IsLoadingEarlierDialog)
            return;

        ScrollDialogToBottom();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsLoadingDialog) && !viewModel.IsLoadingDialog)
            ScrollDialogToBottom();
    }

    private void ScrollDialogToBottom()
    {
        var requestID = ++dialogScrollRequestID;
        var sessionID = viewModel.CurrentSession?.ID;

        Dispatcher.UIThread.Post
        (
            () => ScrollDialogToBottomWhenStable(requestID, sessionID, 0, double.NaN, 0),
            DispatcherPriority.ContextIdle
        );
    }

    private void ScrollDialogToBottomWhenStable
    (
        int    requestID,
        long?  sessionID,
        int    attempt,
        double previousExtent,
        int    stablePasses
    )
    {
        if (requestID != dialogScrollRequestID        ||
            sessionID != viewModel.CurrentSession?.ID ||
            viewModel.IsLoadingDialog)
            return;

        if (dialogScrollViewer is null)
        {
            if (viewModel.Dialog.Entries.Count > 0)
                DialogList.ScrollIntoView(viewModel.Dialog.Entries[^1]);

            return;
        }

        var lastEntryRealized = viewModel.Dialog.Entries.Count == 0 ||
                                DialogList.ContainerFromItem(viewModel.Dialog.Entries[^1]) is not null;
        var markdownCurrent = DialogList.GetVisualDescendants()
                                        .OfType<LiveMarkdownView>()
                                        .All(view => view.IsRenderCurrent);
        var extent = dialogScrollViewer.Extent.Height;

        dialogScrollViewer.ScrollToEnd();

        if (lastEntryRealized             &&
            markdownCurrent               &&
            !double.IsNaN(previousExtent) &&
            Math.Abs(extent - previousExtent) < 0.5)
            stablePasses++;
        else
            stablePasses = 0;

        if (stablePasses >= 2 || attempt >= 31)
            return;

        Dispatcher.UIThread.Post
        (
            () => ScrollDialogToBottomWhenStable(requestID, sessionID, attempt + 1, extent, stablePasses),
            DispatcherPriority.ContextIdle
        );
    }

    private void OnRollbackRound(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: DialogEntryViewModel entry })
        {
            entry.IsMenuOpen = false;
            _                = viewModel.RollbackLastRoundCommand.ExecuteAsync(null);
        }
    }

    private async void OnLoadEarlierDialog(object? sender, RoutedEventArgs e)
    {
        if (dialogScrollViewer is null)
            return;

        var oldExtent = dialogScrollViewer.Extent.Height;
        var oldOffset = dialogScrollViewer.Offset;

        await viewModel.LoadEarlierDialogHistoryAsync();

        Dispatcher.UIThread.Post
        (
            () =>
            {
                if (dialogScrollViewer is null)
                    return;

                var addedHeight = dialogScrollViewer.Extent.Height - oldExtent;
                dialogScrollViewer.Offset = oldOffset.WithY(oldOffset.Y + addedHeight);
            },
            DispatcherPriority.ContextIdle
        );
    }

    private async void OnCopyEntry(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: DialogEntryViewModel entry } element)
        {
            var clipboard = GetTopLevel(element)?.Clipboard;

            if (clipboard is not null)
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateText(entry.Content));
                await clipboard.SetDataAsync(transfer);
            }

            entry.IsMenuOpen = false;
        }
    }

    private void OnEditEntry(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: DialogEntryViewModel entry })
        {
            entry.StartEdit();
            entry.IsMenuOpen = false;
        }
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: DialogEntryViewModel entry })
        {
            entry.IsMenuOpen = !entry.IsMenuOpen;
            e.Handled        = true;
        }
    }

    private void OnImportButtonClick(object sender, RoutedEventArgs e)
    {
        if (isRemote)
        {
            remoteImportMenu!.IsVisible         = true;
            remoteImportMenuBackdrop!.IsVisible = true;
            e.Handled                            = true;
            return;
        }

        if (sender is not Control { ContextMenu: { } menu } element)
            return;

        menu.Open(element);
        e.Handled = true;
    }

    private void OnImportDirectorPrompt(object sender, RoutedEventArgs e) =>
        viewModel.ImportProjectCommand.Execute(null);

    private void OnImportSillyTavern(object sender, RoutedEventArgs e) =>
        viewModel.ImportSillyTavernProjectCommand.Execute(null);

    private void OnRemoteImportMenuBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        remoteImportMenu!.IsVisible         = false;
        remoteImportMenuBackdrop!.IsVisible = false;
    }

    private void OnRemoteImportDirectorPromptClick(object? sender, RoutedEventArgs e)
    {
        remoteImportMenu!.IsVisible         = false;
        remoteImportMenuBackdrop!.IsVisible = false;
        viewModel.ImportProjectCommand.Execute(null);
    }

    private void OnRemoteImportSillyTavernClick(object? sender, RoutedEventArgs e)
    {
        remoteImportMenu!.IsVisible         = false;
        remoteImportMenuBackdrop!.IsVisible = false;
        viewModel.ImportSillyTavernProjectCommand.Execute(null);
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
