using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
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

public partial class MainWindow : FAAppWindow, IRemoteDialogOwner
{
    private readonly MainViewModel       viewModel;
    private readonly bool                isRemote;
    private readonly ILanSharingService? lanSharingService;

    private ListBox       dialogList = null!;
    private ScrollViewer? dialogScrollViewer;

    private int  dialogScrollRequestID;
    private bool isFollowingDialogTail = true;
    private bool isMobileRemote;
    private bool closeAuthorized;
    private bool closeInProgress;

    private Panel?        remoteOverlay;
    private Border?       remoteImportMenu;
    private Border?       remoteProjectMenu;
    private Border?       remoteSessionMenu;
    private Border?       remoteMenu;
    private Control?      remoteMenuOwner;
    private PathComboBox? projectComboBox;

    public IRemoteDialogHost? RemoteDialogHost { get; set; }

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
        RootLayout              = this.FindControl<Grid>(nameof(RootLayout))!;
        MobileSessionsToggle    = this.FindControl<ToggleButton>(nameof(MobileSessionsToggle))!;
        MobileDetailsToggle     = this.FindControl<ToggleButton>(nameof(MobileDetailsToggle))!;
        MobileMoreActionsButton = this.FindControl<ToggleButton>(nameof(MobileMoreActionsButton))!;
        dialogList              = this.FindControl<ListBox>("DialogListBox")!;
        remoteOverlay           = this.FindControl<Panel>(nameof(RemoteOverlay));
        remoteImportMenu        = this.FindControl<Border>(nameof(RemoteImportMenu));
        remoteProjectMenu       = this.FindControl<Border>(nameof(RemoteProjectMenu));
        remoteSessionMenu       = this.FindControl<Border>(nameof(RemoteSessionMenu));
        projectComboBox         = this.FindControl<PathComboBox>(nameof(ProjectComboBox));

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"DirectorPrompt {version}";

        viewModel.Dialog.Entries.CollectionChanged += OnDialogEntriesChanged;
        viewModel.PropertyChanged                  += OnViewModelPropertyChanged;

        if (attachWindowBehavior)
            Loaded += OnLoaded;
    }

    internal void SetRemoteViewportWidth(double width)
    {
        if (!isRemote || width <= 0)
            return;

        var useMobileLayout = width < 720;

        if (useMobileLayout == isMobileRemote)
            return;

        isMobileRemote = useMobileLayout;

        if (useMobileLayout)
            RootLayout.Classes.Add("mobile");
        else
            RootLayout.Classes.Remove("mobile");

        ResetMobileNavigation();
    }

    internal void DisposeRemoteVisual()
    {
        if (!isRemote)
            return;

        viewModel.Dialog.Entries.CollectionChanged -= OnDialogEntriesChanged;
        viewModel.PropertyChanged                  -= OnViewModelPropertyChanged;
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

    private void OnMobileBackdropPointerPressed(object? sender, PointerPressedEventArgs e) =>
        MobileMoreActionsButton.IsChecked = false;

    private void OnMobileMenuItemClick(object? sender, RoutedEventArgs e) =>
        MobileMoreActionsButton.IsChecked = false;

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
        CloseRemoteMenu();
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        dialogScrollViewer = dialogList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        if (dialogScrollViewer is not null)
            dialogScrollViewer.ScrollChanged += OnDialogScrollChanged;

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
            ScrollDialogToBottom(true);
    }

    private void OnDialogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (dialogScrollViewer is null)
            return;

        var maximum = Math.Max(0, dialogScrollViewer.Extent.Height - dialogScrollViewer.Viewport.Height);

        isFollowingDialogTail = maximum - dialogScrollViewer.Offset.Y <= 1;
    }

    private void ScrollDialogToBottom(bool force = false)
    {
        if (!force && !isFollowingDialogTail)
            return;

        var requestID = ++dialogScrollRequestID;
        var sessionID = viewModel.CurrentSession?.ID;

        Dispatcher.UIThread.Post
        (
            () => ScrollDialogToBottomAfterLayout(requestID, sessionID),
            DispatcherPriority.Render
        );
    }

    private void ScrollDialogToBottomAfterLayout(int requestID, long? sessionID)
    {
        if (requestID != dialogScrollRequestID        ||
            sessionID != viewModel.CurrentSession?.ID ||
            viewModel.IsLoadingDialog)
            return;

        dialogScrollViewer ??= dialogList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        if (dialogScrollViewer is null)
            return;

        if (viewModel.Dialog.Entries.Count > 0)
            dialogList.ScrollIntoView(viewModel.Dialog.Entries[^1]);

        dialogScrollViewer.ScrollToEnd();

        Dispatcher.UIThread.Post
        (
            () => CompleteDialogScrollToBottom(requestID, sessionID),
            DispatcherPriority.ContextIdle
        );
    }

    private void CompleteDialogScrollToBottom(int requestID, long? sessionID)
    {
        if (requestID != dialogScrollRequestID        ||
            sessionID != viewModel.CurrentSession?.ID ||
            viewModel.IsLoadingDialog)
            return;

        dialogScrollViewer?.ScrollToEnd();
    }

    private async void OnLoadEarlierDialog(object? sender, RoutedEventArgs e)
    {
        if (dialogScrollViewer is null)
            return;

        var previousExtent = dialogScrollViewer.Extent.Height;
        var previousOffset = dialogScrollViewer.Offset;
        var sessionID      = viewModel.CurrentSession?.ID;

        await viewModel.LoadEarlierDialogHistoryAsync();

        if (sessionID != viewModel.CurrentSession?.ID)
            return;

        Dispatcher.UIThread.Post
        (
            () => RestoreDialogOffset(previousExtent, previousOffset),
            DispatcherPriority.Render
        );
    }

    private void RestoreDialogOffset(double previousExtent, Vector previousOffset)
    {
        if (dialogScrollViewer is null)
            return;

        var maximum     = Math.Max(0, dialogScrollViewer.Extent.Height - dialogScrollViewer.Viewport.Height);
        var addedHeight = Math.Max(0, dialogScrollViewer.Extent.Height - previousExtent);
        var offset      = Math.Clamp(previousOffset.Y                  + addedHeight, 0, maximum);

        dialogScrollViewer.Offset = dialogScrollViewer.Offset.WithY(offset);
    }

    private void OnImportButtonClick(object sender, RoutedEventArgs e)
    {
        if (!isRemote || sender is not Control element)
            return;

        OpenRemoteMenu(element, remoteImportMenu!);
        e.Handled = true;
    }

    private void OnImportDirectorPrompt(object sender, RoutedEventArgs e) =>
        viewModel.ImportProjectCommand.Execute(null);

    private void OnImportSillyTavern(object sender, RoutedEventArgs e) =>
        viewModel.ImportSillyTavernProjectCommand.Execute(null);

    private void OpenRemoteMenu(Control owner, Border menu)
    {
        CloseRemoteMenu();

        (menu.Parent as Panel)?.Children.Remove(menu);
        menu.IsVisible = true;

        if (RemotePopupHost.Show(owner, menu, menu.Width, RestoreRemoteMenu))
        {
            remoteMenu      = menu;
            remoteMenuOwner = owner;
            return;
        }

        RestoreRemoteMenu(menu);
    }

    private void CloseRemoteMenu()
    {
        if (remoteMenuOwner is { } owner)
        {
            var menu = RemotePopupHost.Hide(owner);

            if (menu is not null)
                RestoreRemoteMenu(menu);

            return;
        }

        if (remoteMenu is not null)
            remoteMenu.IsVisible = false;
    }

    private void RestoreRemoteMenu(Control content)
    {
        remoteMenuOwner   = null;
        remoteMenu        = null;
        content.IsVisible = false;

        if (remoteOverlay is not null && content.Parent is null)
            remoteOverlay.Children.Add(content);
    }

    private void OnRemoteImportDirectorPromptClick(object? sender, RoutedEventArgs e)
    {
        CloseRemoteMenu();
        viewModel.ImportProjectCommand.Execute(null);
    }

    private void OnRemoteImportSillyTavernClick(object? sender, RoutedEventArgs e)
    {
        CloseRemoteMenu();
        viewModel.ImportSillyTavernProjectCommand.Execute(null);
    }

    private void OnProjectItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: Project project } item ||
            !e.GetCurrentPoint(item).Properties.IsRightButtonPressed)
            return;

        if (isRemote)
        {
            remoteProjectMenu!.DataContext = project;
            OpenRemoteMenu(projectComboBox!, remoteProjectMenu);
        }
        else
            OpenLocalMenu(item, project);

        e.Handled = true;
    }

    private void OnSessionItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: Session session } item ||
            !e.GetCurrentPoint(item).Properties.IsRightButtonPressed)
            return;

        if (isRemote)
        {
            remoteSessionMenu!.DataContext = session;
            OpenRemoteMenu(item, remoteSessionMenu);
        }
        else
            OpenLocalMenu(item, session);

        e.Handled = true;
    }

    private void OnProjectItemContextRequested(object? sender, ContextRequestedEventArgs e) =>
        e.Handled = true;

    private void OnSessionItemContextRequested(object? sender, ContextRequestedEventArgs e) =>
        e.Handled = true;

    private static void OpenLocalMenu(Control owner, object item)
    {
        if (owner.ContextFlyout is not MenuFlyout menu)
            return;

        foreach (var menuItem in menu.Items.OfType<MenuItem>())
            menuItem.Tag = item;

        menu.ShowAt(owner);
    }

    private void OnEditProjectItem(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: Project project })
            return;

        CloseRemoteMenu();
        viewModel.CurrentProject = project;
        viewModel.EditProjectCommand.Execute(null);
    }

    private void OnExportProjectItem(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: Project project })
            return;

        CloseRemoteMenu();
        viewModel.CurrentProject = project;
        viewModel.ExportProjectCommand.Execute(null);
    }

    private async void OnDeleteProjectItem(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: Project project })
            return;

        CloseRemoteMenu();
        var message = Loc.Get("Dialog.ConfirmDeleteProject", project.Name);

        if (await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), message, true))
            _ = viewModel.DeleteProjectCommand.ExecuteAsync(project);
    }

    private async void OnRenameSessionItem(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: Session session })
            return;

        CloseRemoteMenu();
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
        if (sender is not Control { Tag: Session session })
            return;

        CloseRemoteMenu();
        var message = Loc.Get("Dialog.ConfirmDeleteSession", session.Title);

        if (await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), message, true))
            _ = viewModel.DeleteSessionCommand.ExecuteAsync(session);
    }
}
