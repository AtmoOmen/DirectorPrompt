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
    private readonly MainViewModel viewModel;
    private readonly bool isRemote;
    private readonly ILanSharingService? lanSharingService;

    private ScrollViewer? dialogScrollViewer;
    private int           dialogScrollRequestID;
    private bool          isMobileRemote;
    private bool          closeAuthorized;
    private bool          closeInProgress;

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
        this.lanSharingService = lanSharingService;
        Closing               += OnClosing;
    }

    internal MainWindow(MainViewModel viewModel, bool attachWindowBehavior)
    {
        this.viewModel = viewModel;
        isRemote       = !attachWindowBehavior;
        DataContext    = viewModel;
        AvaloniaXamlLoader.Load(this);
        RootLayout            = this.FindControl<Grid>(nameof(RootLayout))!;
        ProjectLabel          = this.FindControl<TextBlock>(nameof(ProjectLabel))!;
        ProjectComboBox       = this.FindControl<PathComboBox>(nameof(ProjectComboBox))!;
        EditProjectButton     = this.FindControl<Button>(nameof(EditProjectButton))!;
        NewProjectButton      = this.FindControl<Button>(nameof(NewProjectButton))!;
        ImportButton          = this.FindControl<Button>(nameof(ImportButton))!;
        LanSharingButton      = this.FindControl<Button>(nameof(LanSharingButton))!;
        SettingsButton        = this.FindControl<Button>(nameof(SettingsButton))!;
        MobileProjectToolbar  = this.FindControl<Grid>(nameof(MobileProjectToolbar))!;
        WorkspaceGrid         = this.FindControl<Grid>(nameof(WorkspaceGrid))!;
        SessionSidebar        = this.FindControl<Border>(nameof(SessionSidebar))!;
        ConversationPanel     = this.FindControl<Grid>(nameof(ConversationPanel))!;
        WorkspaceSplitter     = this.FindControl<GridSplitter>(nameof(WorkspaceSplitter))!;
        DetailsPanel          = this.FindControl<TabControl>(nameof(DetailsPanel))!;
        MessageRail           = this.FindControl<MessageRail>(nameof(MessageRail))!;
        MobileMessageRail     = this.FindControl<MessageRail>(nameof(MobileMessageRail))!;
        CollapseSidebarButton = this.FindControl<Button>(nameof(CollapseSidebarButton))!;
        ExpandSidebarButton   = this.FindControl<Button>(nameof(ExpandSidebarButton))!;
        MobileNavigation         = this.FindControl<Border>(nameof(MobileNavigation))!;
        MobileSessionsButton     = this.FindControl<ToggleButton>(nameof(MobileSessionsButton))!;
        MobileConversationButton = this.FindControl<ToggleButton>(nameof(MobileConversationButton))!;
        MobileDetailsButton      = this.FindControl<ToggleButton>(nameof(MobileDetailsButton))!;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"DirectorPrompt {version}";

        if (attachWindowBehavior)
        {
            viewModel.Dialog.Entries.CollectionChanged += OnDialogEntriesChanged;
            viewModel.PropertyChanged                  += OnViewModelPropertyChanged;
            Loaded                                     += OnLoaded;
        }
        else
        {
            RootLayout.SizeChanged += OnRemoteRootSizeChanged;
        }
    }

    private void OnRemoteRootSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!isRemote || e.NewSize.Width <= 0)
            return;

        var useMobileLayout = e.NewSize.Width < 720;

        if (useMobileLayout != isMobileRemote)
        {
            isMobileRemote = useMobileLayout;
            ApplyRemoteLayout(useMobileLayout);
        }

        if (useMobileLayout)
        {
            ProjectComboBox.Width = Math.Max(240, e.NewSize.Width - 24);
            SessionSidebar.Width  = Math.Min(320, Math.Max(280, e.NewSize.Width * 0.86));
        }
    }

    private void ApplyRemoteLayout(bool mobile)
    {
        ProjectLabel.IsVisible          = !mobile;
        EditProjectButton.IsVisible     = !mobile;
        NewProjectButton.IsVisible      = !mobile;
        ImportButton.IsVisible          = !mobile;
        LanSharingButton.IsVisible      = !mobile && viewModel.LanSharingService.IsActive;
        SettingsButton.IsVisible        = !mobile;
        MobileProjectToolbar.IsVisible  = mobile;
        WorkspaceSplitter.IsVisible     = !mobile;
        CollapseSidebarButton.IsVisible = !mobile && viewModel.IsSessionSidebarExpanded;
        ExpandSidebarButton.IsVisible   = !mobile && !viewModel.IsSessionSidebarExpanded;
        MobileNavigation.IsVisible      = mobile;
        MessageRail.IsVisible           = !mobile;
        MobileMessageRail.IsVisible     = false;

        if (mobile)
        {
            WorkspaceGrid.ColumnDefinitions[0].Width    = new GridLength(0);
            WorkspaceGrid.ColumnDefinitions[1].Width    = new GridLength(1, GridUnitType.Star);
            WorkspaceGrid.ColumnDefinitions[1].MinWidth = 0;
            WorkspaceGrid.ColumnDefinitions[2].Width    = new GridLength(0);
            WorkspaceGrid.ColumnDefinitions[3].Width    = new GridLength(0);
            WorkspaceGrid.ColumnDefinitions[3].MinWidth = 0;

            Grid.SetColumn(SessionSidebar, 0);
            Grid.SetColumnSpan(SessionSidebar, 4);
            Grid.SetColumn(DetailsPanel, 0);
            Grid.SetColumnSpan(DetailsPanel, 4);
            SessionSidebar.SetValue(Panel.ZIndexProperty, 10);
            DetailsPanel.SetValue(Panel.ZIndexProperty, 10);
            SessionSidebar.Margin      = new Thickness(0, 0, 0, 59);
            ConversationPanel.Margin   = new Thickness(0, 0, 0, 59);
            DetailsPanel.Margin        = new Thickness(0, 0, 0, 59);
            ShowMobileConversation();
            return;
        }

        ProjectComboBox.Width = 300;
        WorkspaceGrid.ColumnDefinitions[0].Width    = GridLength.Auto;
        WorkspaceGrid.ColumnDefinitions[1].Width    = new GridLength(1, GridUnitType.Star);
        WorkspaceGrid.ColumnDefinitions[1].MinWidth = 400;
        WorkspaceGrid.ColumnDefinitions[2].Width    = GridLength.Auto;
        WorkspaceGrid.ColumnDefinitions[3].Width    = new GridLength(320);
        WorkspaceGrid.ColumnDefinitions[3].MinWidth = 260;

        Grid.SetColumn(SessionSidebar, 0);
        Grid.SetColumnSpan(SessionSidebar, 1);
        Grid.SetColumn(DetailsPanel, 3);
        Grid.SetColumnSpan(DetailsPanel, 1);
        SessionSidebar.SetValue(Panel.ZIndexProperty, 0);
        DetailsPanel.SetValue(Panel.ZIndexProperty, 0);
        SessionSidebar.Width          = 240;
        SessionSidebar.Margin         = default;
        SessionSidebar.IsVisible      = viewModel.IsSessionSidebarExpanded;
        ConversationPanel.IsVisible  = true;
        ConversationPanel.Margin     = default;
        DetailsPanel.IsVisible       = true;
        DetailsPanel.Margin          = new Thickness(8, 4, 8, 8);
    }

    private void OnMobileSessionsClick(object? sender, RoutedEventArgs e)
    {
        if (!isMobileRemote)
            return;

        SessionSidebar.IsVisible     = true;
        ConversationPanel.IsVisible = true;
        DetailsPanel.IsVisible      = false;
        MobileMessageRail.IsVisible        = false;
        MobileSessionsButton.IsChecked     = true;
        MobileConversationButton.IsChecked = false;
        MobileDetailsButton.IsChecked      = false;
    }

    private void OnMobileConversationClick(object? sender, RoutedEventArgs e) =>
        ShowMobileConversation();

    private void OnMobileDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (!isMobileRemote)
            return;

        SessionSidebar.IsVisible     = false;
        ConversationPanel.IsVisible = true;
        DetailsPanel.IsVisible      = true;
        MobileMessageRail.IsVisible        = false;
        MobileSessionsButton.IsChecked     = false;
        MobileConversationButton.IsChecked = false;
        MobileDetailsButton.IsChecked      = true;
    }

    private void OnSessionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isMobileRemote)
            ShowMobileConversation();
    }

    private void ShowMobileConversation()
    {
        if (!isMobileRemote)
            return;

        SessionSidebar.IsVisible     = false;
        ConversationPanel.IsVisible = true;
        DetailsPanel.IsVisible      = false;
        MobileMessageRail.IsVisible        = true;
        MobileSessionsButton.IsChecked     = false;
        MobileConversationButton.IsChecked = true;
        MobileDetailsButton.IsChecked      = false;
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
        if (requestID != dialogScrollRequestID ||
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

        if (lastEntryRealized && markdownCurrent &&
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
            _ = viewModel.RollbackLastRoundCommand.ExecuteAsync(null);
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
            var clipboard = TopLevel.GetTopLevel(element)?.Clipboard;

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
