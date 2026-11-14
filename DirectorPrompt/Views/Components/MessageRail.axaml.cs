using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class MessageRail : UserControl
{
    public static readonly StyledProperty<IEnumerable?> EntriesProperty =
        AvaloniaProperty.Register<MessageRail, IEnumerable?>(nameof(Entries));

    public static readonly StyledProperty<DialogMessageList?> TargetDialogMessagesProperty =
        AvaloniaProperty.Register<MessageRail, DialogMessageList?>(nameof(TargetDialogMessages));

    private ScrollViewer? railScrollViewer;
    private ScrollViewer? targetScrollViewer;
    private bool          isPointerOver;
    private bool          synchronizationScheduled;

    private ListBox Rail =>
        this.GetLogicalDescendants().OfType<ListBox>().First(control => control.Name == "RailListBox");

    public IEnumerable? Entries
    {
        get => GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public DialogMessageList? TargetDialogMessages
    {
        get => GetValue(TargetDialogMessagesProperty);
        set => SetValue(TargetDialogMessagesProperty, value);
    }

    static MessageRail() =>
        TargetDialogMessagesProperty.Changed.AddClassHandler<MessageRail>
        (static (rail, _) => rail.HookTargetScrollViewer()
        );

    public MessageRail()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded                 += OnLoaded;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (targetScrollViewer is not null)
            targetScrollViewer.ScrollChanged -= OnTargetScrollChanged;
        targetScrollViewer = null;
        railScrollViewer   = null;
    }

    private void OnLoaded
    (
        object?         sender,
        RoutedEventArgs e
    )
    {
        railScrollViewer = Rail.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        Dispatcher.UIThread.Post(HookTargetScrollViewer, DispatcherPriority.Loaded);
    }

    private void HookTargetScrollViewer()
    {
        var scrollViewer = TargetDialogMessages?.ScrollViewer;

        if (ReferenceEquals(scrollViewer, targetScrollViewer))
            return;

        if (targetScrollViewer is not null)
            targetScrollViewer.ScrollChanged -= OnTargetScrollChanged;

        targetScrollViewer = scrollViewer;

        if (targetScrollViewer is not null)
        {
            targetScrollViewer.ScrollChanged += OnTargetScrollChanged;
            ScheduleSynchronization();
        }
    }

    private void OnTargetScrollChanged
    (
        object?                sender,
        ScrollChangedEventArgs e
    )
    {
        if (!ReferenceEquals(e.Source, targetScrollViewer))
            return;

        ScheduleSynchronization();
    }

    private void ScheduleSynchronization()
    {
        if (isPointerOver || synchronizationScheduled)
            return;

        synchronizationScheduled = true;
        Dispatcher.UIThread.Post
        (
            () =>
            {
                try
                {
                    if (!isPointerOver)
                        SyncRailToTargetScroll();
                }
                finally
                {
                    synchronizationScheduled = false;
                }
            },
            DispatcherPriority.Loaded
        );
    }

    private void SyncRailToTargetScroll()
    {
        if (targetScrollViewer is null || TargetDialogMessages is null || railScrollViewer is null)
            return;

        DialogEntryViewModel? topEntry    = null;
        var                   minDistance = double.MaxValue;

        foreach (var (entry, container) in TargetDialogMessages.GetRealizedEntries())
        {
            var position = container.TranslatePoint(new Point(0, 0), targetScrollViewer);

            if (!position.HasValue)
                continue;

            var y      = position.Value.Y;
            var height = container.Bounds.Height;

            if (y <= 1 && y + height > 1)
            {
                topEntry = entry;
                break;
            }

            var distance = Math.Abs(y);

            if (distance < minDistance)
            {
                minDistance = distance;
                topEntry    = entry;
            }
        }

        if (topEntry is not null && !ReferenceEquals(Rail.SelectedItem, topEntry))
        {
            Rail.SelectedItem = topEntry;

            Rail.ScrollIntoView(topEntry);
        }
    }

    private void OnRailItemPointerPressed
    (
        object?                 sender,
        PointerPressedEventArgs e
    )
    {
        if (sender is not Control { DataContext: DialogEntryViewModel entry } || TargetDialogMessages is null)
            return;

        TargetDialogMessages.ScrollIntoView(entry);
        e.Handled = true;
    }

    private void OnRailPointerEntered
    (
        object?          sender,
        PointerEventArgs e
    ) =>
        isPointerOver = true;

    private void OnRailPointerExited
    (
        object?          sender,
        PointerEventArgs e
    )
    {
        isPointerOver = false;
        ScheduleSynchronization();
    }

    private void OnGoToTopClick
    (
        object?         sender,
        RoutedEventArgs e
    )
    {
        if (Entries?.OfType<DialogEntryViewModel>().FirstOrDefault() is { } firstEntry)
            TargetDialogMessages?.ScrollIntoView(firstEntry);
    }

    private void OnGoToBottomClick
    (
        object?         sender,
        RoutedEventArgs e
    ) => TargetDialogMessages?.ScrollToEnd();
}
