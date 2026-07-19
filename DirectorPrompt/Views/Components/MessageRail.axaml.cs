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

    public static readonly StyledProperty<ListBox?> TargetListBoxProperty =
        AvaloniaProperty.Register<MessageRail, ListBox?>(nameof(TargetListBox));

    private ScrollViewer? railScrollViewer;
    private ScrollViewer? targetScrollViewer;
    private bool          isPointerOver;
    private int           navigationRequestID;

    private ListBox Rail =>
        this.GetLogicalDescendants().OfType<ListBox>().First(control => control.Name == "RailListBox");

    public IEnumerable? Entries
    {
        get => GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public ListBox? TargetListBox
    {
        get => GetValue(TargetListBoxProperty);
        set => SetValue(TargetListBoxProperty, value);
    }

    static MessageRail() =>
        TargetListBoxProperty.Changed.AddClassHandler<MessageRail>
        (static (rail, _) => rail.HookTargetScrollViewer()
        );

    public MessageRail()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        railScrollViewer = Rail.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        HookTargetScrollViewer();
    }

    private void HookTargetScrollViewer()
    {
        var scrollViewer = TargetListBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        if (ReferenceEquals(scrollViewer, targetScrollViewer))
            return;

        if (targetScrollViewer is not null)
            targetScrollViewer.ScrollChanged -= OnTargetScrollChanged;

        targetScrollViewer = scrollViewer;

        if (targetScrollViewer is not null)
            targetScrollViewer.ScrollChanged += OnTargetScrollChanged;
    }

    private void OnTargetScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // 鼠标悬停在 MessageRail 上时不进行位置偏移同步，防止闪现和鼠标底下元素移位
        if (isPointerOver)
            return;

        SyncRailToTargetScroll();
    }

    private void SyncRailToTargetScroll()
    {
        if (targetScrollViewer is null || TargetListBox is null || railScrollViewer is null)
            return;

        var items = TargetListBox.Items;

        if (items.Count == 0)
            return;

        var panel = TargetListBox.GetVisualDescendants().OfType<VirtualizingStackPanel>().FirstOrDefault();
        var firstIndex = panel is { FirstRealizedIndex: >= 0 } ?
                             panel.FirstRealizedIndex :
                             0;
        var lastIndex = panel is { LastRealizedIndex: >= 0 } ?
                            panel.LastRealizedIndex :
                            items.Count - 1;

        object? topEntry    = null;
        var     targetIndex = -1;
        var     minDistance = double.MaxValue;

        for (var index = firstIndex; index <= lastIndex; index++)
        {
            var item = items[index];

            if (item is null)
                continue;

            var container = TargetListBox.ContainerFromItem(item);

            if (container is not null)
            {
                var position = container.TranslatePoint(new Point(0, 0), targetScrollViewer);

                if (position.HasValue)
                {
                    var y      = position.Value.Y;
                    var height = container.Bounds.Height;

                    if (y <= 1 && y + height > 1)
                    {
                        topEntry    = item;
                        targetIndex = index;
                        break;
                    }

                    var distance = Math.Abs(y);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        topEntry    = item;
                        targetIndex = index;
                    }
                }
            }
        }

        if (topEntry is not null)
        {
            Rail.SelectedItem = topEntry;

            if (targetIndex >= 0 && items.Count > 1)
            {
                double lastItemHeight = 80;
                var    lastContainer  = TargetListBox.ContainerFromIndex(items.Count - 1);

                if (lastContainer is not null)
                    lastItemHeight = lastContainer.Bounds.Height;

                var effectiveMaximum = targetScrollViewer.Extent.Height - Math.Max(targetScrollViewer.Viewport.Height, lastItemHeight);
                var railMaximum      = Math.Max(0, railScrollViewer.Extent.Height - railScrollViewer.Viewport.Height);

                if (effectiveMaximum > 0 && railMaximum > 0)
                {
                    var ratio = Math.Clamp(targetScrollViewer.Offset.Y / effectiveMaximum, 0, 1);
                    railScrollViewer.Offset = railScrollViewer.Offset.WithY(ratio * railMaximum);
                }
            }
        }
    }

    private void OnRailItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: DialogEntryViewModel entry } || TargetListBox is null)
            return;

        var requestID = ++navigationRequestID;

        Dispatcher.UIThread.Post
        (
            () => ScrollToEntry(requestID, entry),
            DispatcherPriority.Background
        );

        e.Handled = true;
    }

    private void ScrollToEntry(int requestID, DialogEntryViewModel entry)
    {
        if (requestID != navigationRequestID || TargetListBox is null)
            return;

        TargetListBox.ScrollIntoView(entry);

        Dispatcher.UIThread.Post
        (
            () => AlignEntryToTop(requestID, entry),
            DispatcherPriority.Render
        );
    }

    private void AlignEntryToTop(int requestID, DialogEntryViewModel entry)
    {
        if (requestID != navigationRequestID || TargetListBox is null)
            return;

        targetScrollViewer ??= TargetListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        if (targetScrollViewer is null)
            return;

        var container = TargetListBox.ContainerFromItem(entry);

        if (container is null)
            return;

        var position = container.TranslatePoint(new Point(0, 0), targetScrollViewer);

        if (!position.HasValue)
            return;

        var maximum = Math.Max(0, targetScrollViewer.Extent.Height - targetScrollViewer.Viewport.Height);
        var offset  = Math.Clamp(targetScrollViewer.Offset.Y       + position.Value.Y, 0, maximum);

        targetScrollViewer.Offset = targetScrollViewer.Offset.WithY(offset);
    }

    private void OnRailPointerEntered(object? sender, PointerEventArgs e) =>
        isPointerOver = true;

    private void OnRailPointerExited(object? sender, PointerEventArgs e)
    {
        isPointerOver = false;
        SyncRailToTargetScroll();
    }

    private void OnGoToTopClick(object? sender, RoutedEventArgs e)
    {
        targetScrollViewer ??= TargetListBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        if (targetScrollViewer is not null)
        {
            targetScrollViewer.Offset = targetScrollViewer.Offset.WithY(0);
        }
    }

    private void OnGoToBottomClick(object? sender, RoutedEventArgs e)
    {
        if (TargetListBox is null)
            return;

        var items = TargetListBox.Items;
        if (items.Count == 0)
            return;

        var lastEntry = items[^1];
        if (lastEntry is DialogEntryViewModel entry)
        {
            var requestID = ++navigationRequestID;
            TargetListBox.ScrollIntoView(entry);
            Dispatcher.UIThread.Post
            (
                () => AlignEntryToTop(requestID, entry),
                DispatcherPriority.Render
            );
        }
    }
}
