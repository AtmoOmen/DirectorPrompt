using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
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
    private bool isPointerOver;

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
        TargetListBoxProperty.Changed.AddClassHandler<MessageRail>(static (rail, _) => rail.HookTargetScrollViewer());

    public MessageRail()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
        if (targetScrollViewer is null || TargetListBox is null || Entries is null || railScrollViewer is null)
            return;

        object? topEntry = null;
        double minDistance = double.MaxValue;

        // 1. 查找当前最顶部的消息
        foreach (object? item in Entries)
        {
            if (item is null)
                continue;

            var container = TargetListBox.ContainerFromItem(item);
            if (container is not null)
            {
                var position = container.TranslatePoint(new Point(0, 0), targetScrollViewer);
                if (position.HasValue)
                {
                    double y = position.Value.Y;
                    double height = container.Bounds.Height;

                    if (y <= 1 && y + height > 1)
                    {
                        topEntry = item;
                        break;
                    }

                    double distance = Math.Abs(y);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        topEntry = item;
                    }
                }
            }
        }

        if (topEntry is not null)
        {
            // 2. 同步设置选中项以渲染非悬浮状态下的高亮指示
            Rail.SelectedItem = topEntry;

            // 3. 统计总数、置顶项索引并取得最后一个项
            int totalCount = 0;
            int targetIndex = -1;
            int i = 0;
            object? lastItem = null;
            foreach (object? item in Entries)
            {
                if (item is null)
                    continue;

                if (ReferenceEquals(item, topEntry))
                {
                    targetIndex = i;
                }
                lastItem = item;
                totalCount++;
                i++;
            }

            if (targetIndex >= 0 && totalCount > 1)
            {
                // 获取最后一条消息的实际高度
                double lastItemHeight = 80;
                if (lastItem is not null)
                {
                    var lastContainer = TargetListBox.ContainerFromItem(lastItem);
                    if (lastContainer is not null)
                    {
                        lastItemHeight = lastContainer.Bounds.Height;
                    }
                }

                // 物理滚动有效高度：Extent.Height - Math.Max(Viewport.Height, lastItemHeight)
                var effectiveMaximum = targetScrollViewer.Extent.Height - Math.Max(targetScrollViewer.Viewport.Height, lastItemHeight);
                var railMaximum = Math.Max(0, railScrollViewer.Extent.Height - railScrollViewer.Viewport.Height);

                if (effectiveMaximum > 0 && railMaximum > 0)
                {
                    // 计算在此有效高度下的滚动比例，超出部分限制在 1.0
                    double ratio = Math.Clamp(targetScrollViewer.Offset.Y / effectiveMaximum, 0, 1);
                    railScrollViewer.Offset = railScrollViewer.Offset.WithY(ratio * railMaximum);
                }
            }
        }
    }

    private void OnRailItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: DialogEntryViewModel entry } || TargetListBox is null)
            return;

        TargetListBox.ScrollIntoView(entry);
        Dispatcher.UIThread.Post(() =>
        {
            if (targetScrollViewer is null)
                return;

            var container = TargetListBox.ContainerFromItem(entry);
            if (container is null)
                return;

            var position = container.TranslatePoint(new Point(0, 0), targetScrollViewer);
            if (position.HasValue)
            {
                var newY = targetScrollViewer.Offset.Y + position.Value.Y;
                var maximum = Math.Max(0, targetScrollViewer.Extent.Height - targetScrollViewer.Viewport.Height);
                targetScrollViewer.Offset = targetScrollViewer.Offset.WithY(Math.Clamp(newY, 0, maximum));
            }
        }, DispatcherPriority.Background);

        e.Handled = true;
    }

    private void OnRailPointerEntered(object? sender, PointerEventArgs e)
    {
        isPointerOver = true;
    }

    private void OnRailPointerExited(object? sender, PointerEventArgs e)
    {
        isPointerOver = false;
        SyncRailToTargetScroll();
    }
}
