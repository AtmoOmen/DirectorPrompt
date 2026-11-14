using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class DialogMessageList : UserControl
{
    public static readonly StyledProperty<IEnumerable?> EntriesProperty =
        AvaloniaProperty.Register<DialogMessageList, IEnumerable?>(nameof(Entries));

    public static readonly StyledProperty<Control?> RailContentProperty =
        AvaloniaProperty.Register<DialogMessageList, Control?>(nameof(RailContent));

    private readonly ListBox   dialogEntriesListBox;
    private readonly ScrollBar messageScrollBar;

    private INotifyCollectionChanged? observedEntries;
    private bool                      isFollowingTail = true;
    private bool                      scrollScheduled;
    private bool                      userScrollPending;
    private DialogEntryViewModel?     navigationEntry;
    private double                    navigationOffset;

    public IEnumerable? Entries
    {
        get => GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public ScrollViewer? ScrollViewer { get; private set; }

    public Control? RailContent
    {
        get => GetValue(RailContentProperty);
        set => SetValue(RailContentProperty, value);
    }

    static DialogMessageList() =>
        EntriesProperty.Changed.AddClassHandler<DialogMessageList>
            (static (list, _) => list.ObserveEntries());

    public DialogMessageList()
    {
        AvaloniaXamlLoader.Load(this);
        dialogEntriesListBox   =  this.FindControl<ListBox>(nameof(DialogEntriesListBox))!;
        messageScrollBar       =  this.FindControl<ScrollBar>("MessageScrollBar")!;
        AttachedToVisualTree   += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent,      OnPointerPressed,      RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent,             OnKeyDown,             RoutingStrategies.Tunnel);
    }

    public void ScrollIntoView
    (
        DialogEntryViewModel entry
    ) => RestoreScrollAnchor(entry, 0);

    public Control? ContainerFromItem
    (
        DialogEntryViewModel entry
    ) =>
        dialogEntriesListBox.ContainerFromItem(entry);

    public IEnumerable<(DialogEntryViewModel Entry, Control Container)> GetRealizedEntries()
    {
        foreach (var container in dialogEntriesListBox.GetRealizedContainers())
        {
            if (dialogEntriesListBox.ItemFromContainer(container) is DialogEntryViewModel entry)
                yield return (entry, container);
        }
    }

    public (DialogEntryViewModel? Entry, double Offset) CaptureScrollAnchor()
    {
        var scrollViewer = ScrollViewer;
        isFollowingTail = false;
        navigationEntry = null;
        if (scrollViewer is null)
            return (null, 0);

        foreach (var (entry, container) in GetRealizedEntries())
        {
            var position = container.TranslatePoint(default, scrollViewer);
            if (position is { } point && point.Y + container.Bounds.Height > 0)
                return (entry, point.Y);
        }

        return (null, 0);
    }

    public void RestoreScrollAnchor(DialogEntryViewModel? entry, double offset)
    {
        if (entry is null)
            return;

        isFollowingTail   = false;
        userScrollPending = false;
        navigationEntry   = entry;
        navigationOffset  = offset;
        if (ContainerFromItem(entry) is null)
            dialogEntriesListBox.ScrollIntoView(entry);
        ScheduleScroll();
    }

    public void ScrollToEnd()
    {
        navigationEntry   = null;
        userScrollPending = false;
        isFollowingTail   = true;
        ScheduleScroll();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        dialogEntriesListBox.ApplyTemplate();
        ScrollViewer = dialogEntriesListBox.GetVisualDescendants().OfType<ScrollViewer>()
                                           .FirstOrDefault(control => control.Name == "PART_ScrollViewer");
        if (ScrollViewer is not null)
            ScrollViewer.ScrollChanged += OnScrollChanged;
        SynchronizeScrollBar();
        LayoutUpdated += OnLayoutUpdated;
        ObserveEntries();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (ScrollViewer is not null)
            ScrollViewer.ScrollChanged -= OnScrollChanged;
        if (observedEntries is not null)
            observedEntries.CollectionChanged -= OnEntriesChanged;
        observedEntries =  null;
        ScrollViewer    =  null;
        LayoutUpdated   -= OnLayoutUpdated;
    }

    private void ObserveEntries()
    {
        if (observedEntries is not null)
            observedEntries.CollectionChanged -= OnEntriesChanged;
        observedEntries = Entries as INotifyCollectionChanged;
        if (observedEntries is not null)
            observedEntries.CollectionChanged += OnEntriesChanged;
        navigationEntry = null;
        ScheduleScroll();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            navigationEntry = null;
        ScheduleScroll();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = ScrollViewer;
        if (!ReferenceEquals(e.Source, scrollViewer))
            return;

        SynchronizeScrollBar();

        if (userScrollPending && e.OffsetDelta.Y != 0 && e.ExtentDelta.Y == 0 && e.ViewportDelta.Y == 0 && scrollViewer is not null)
        {
            isFollowingTail   = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y <= 2;
            userScrollPending = false;
        }

        if (isFollowingTail)
            ScheduleScroll();
    }

    private void SynchronizeScrollBar()
    {
        if (ScrollViewer is not { } scrollViewer)
            return;

        messageScrollBar.Maximum      = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        messageScrollBar.ViewportSize = scrollViewer.Viewport.Height;
        messageScrollBar.LargeChange  = scrollViewer.LargeChange.Height;
        messageScrollBar.SmallChange  = scrollViewer.SmallChange.Height;
        messageScrollBar.Value        = scrollViewer.Offset.Y;
    }

    private void OnScrollBarScroll(object? sender, ScrollEventArgs e)
    {
        if (ScrollViewer is not { } scrollViewer)
            return;

        navigationEntry     = null;
        userScrollPending   = false;
        isFollowingTail     = messageScrollBar.Maximum - e.NewValue <= 2;
        scrollViewer.Offset = scrollViewer.Offset.WithY(e.NewValue);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
            return;
        isFollowingTail   = false;
        navigationEntry   = null;
        userScrollPending = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<Expander>() is not null)
        {
            isFollowingTail   = false;
            navigationEntry   = null;
            userScrollPending = false;
            return;
        }

        if (e.Pointer.Type != PointerType.Touch && e.Source is not ScrollBar && (e.Source as Visual)?.FindAncestorOfType<ScrollBar>() is null)
            return;
        isFollowingTail   = false;
        navigationEntry   = null;
        userScrollPending = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End))
            return;
        isFollowingTail   = false;
        navigationEntry   = null;
        userScrollPending = true;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (isFollowingTail || navigationEntry is not null)
            ScheduleScroll();
    }

    private void ScheduleScroll()
    {
        if (scrollScheduled)
            return;

        scrollScheduled = true;
        Dispatcher.UIThread.Post(ApplyScroll, DispatcherPriority.Loaded);
    }

    private void ApplyScroll()
    {
        var scrollViewer = ScrollViewer;
        scrollScheduled = false;
        if (scrollViewer is null || (!isFollowingTail && navigationEntry is null))
            return;

        var entry = navigationEntry;
        if (entry is null && dialogEntriesListBox.ItemCount > 0)
            entry = dialogEntriesListBox.Items[^1] as DialogEntryViewModel;
        if (entry is null)
            return;

        var container = ContainerFromItem(entry);

        if (container is null)
        {
            dialogEntriesListBox.ScrollIntoView(entry);
            return;
        }

        var position = container.TranslatePoint(default, scrollViewer);
        if (position is null)
            return;

        var delta = navigationEntry is not null ?
                        position.Value.Y                           - navigationOffset :
                        position.Value.Y + container.Bounds.Height - scrollViewer.Viewport.Height;
        var maximum = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var target  = Math.Clamp(scrollViewer.Offset.Y       + delta, 0, maximum);
        if (Math.Abs(target - scrollViewer.Offset.Y) > 0.5)
            scrollViewer.Offset = scrollViewer.Offset.WithY(target);
        else
            navigationEntry = null;
    }
}
