using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class DialogMessageList : UserControl
{
    public static readonly StyledProperty<IEnumerable?> StableEntriesProperty =
        AvaloniaProperty.Register<DialogMessageList, IEnumerable?>(nameof(StableEntries));

    public static readonly StyledProperty<DialogEntryViewModel?> StreamingEntryProperty =
        AvaloniaProperty.Register<DialogMessageList, DialogEntryViewModel?>(nameof(StreamingEntry));

    public static readonly StyledProperty<bool> HasStreamingEntryProperty =
        AvaloniaProperty.Register<DialogMessageList, bool>(nameof(HasStreamingEntry));

    private readonly ListBox dialogEntriesListBox;

    private ScrollViewer? hookedScrollViewer;
    private bool          isFollowingTail = true;
    private bool          tailScrollScheduled;
    private bool          suppressFollowUpdate;
    private int           tailRetryCount;

    public IEnumerable? StableEntries
    {
        get => GetValue(StableEntriesProperty);
        set => SetValue(StableEntriesProperty, value);
    }

    public DialogEntryViewModel? StreamingEntry
    {
        get => GetValue(StreamingEntryProperty);
        set => SetValue(StreamingEntryProperty, value);
    }

    public bool HasStreamingEntry
    {
        get => GetValue(HasStreamingEntryProperty);
        private set => SetValue(HasStreamingEntryProperty, value);
    }

    /// <summary>
    /// 稳定消息与流式消息共处同一个滚动视图，该视图由列表模板提供。
    /// </summary>
    public ScrollViewer? ScrollViewer =>
        field ??= dialogEntriesListBox.GetVisualDescendants()
                                      .OfType<ScrollViewer>()
                                      .FirstOrDefault(control => control.Name == "PART_ScrollViewer");

    private ContentPresenter? StreamingEntryPresenter =>
        field ??= dialogEntriesListBox.GetVisualDescendants()
                                      .OfType<ContentPresenter>()
                                      .FirstOrDefault(control => control.Name == "StreamingEntryPresenter");

    static DialogMessageList() =>
        StreamingEntryProperty.Changed.AddClassHandler<DialogMessageList>
            (static (list, _) => list.OnStreamingEntryChanged());

    public DialogMessageList()
    {
        AvaloniaXamlLoader.Load(this);
        dialogEntriesListBox =  this.FindControl<ListBox>(nameof(DialogEntriesListBox))!;
        LayoutUpdated        += OnLayoutUpdated;
        SizeChanged          += OnSizeChanged;
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    public void ScrollIntoView
    (
        DialogEntryViewModel entry
    )
    {
        if (ReferenceEquals(entry, StreamingEntry))
        {
            StreamingEntryPresenter?.BringIntoView();
            return;
        }

        dialogEntriesListBox.ScrollIntoView(entry);
    }

    public Control? ContainerFromItem
    (
        DialogEntryViewModel entry
    ) =>
        ReferenceEquals(entry, StreamingEntry) ?
            StreamingEntryPresenter :
            dialogEntriesListBox.ContainerFromItem(entry);

    public IEnumerable<(DialogEntryViewModel Entry, Control Container)> GetRealizedEntries()
    {
        foreach (var container in dialogEntriesListBox.GetRealizedContainers())
        {
            if (dialogEntriesListBox.ItemFromContainer(container) is DialogEntryViewModel entry)
                yield return (entry, container);
        }

        if (StreamingEntry is not null && StreamingEntryPresenter is { IsVisible: true } presenter)
            yield return (StreamingEntry, presenter);
    }

    /// <summary>
    /// 列表模板在首次布局时才建立滚动视图，布局完成后订阅一次即可。
    /// </summary>
    private void OnLayoutUpdated
    (
        object?   sender,
        EventArgs e
    )
    {
        if (ScrollViewer is null)
            return;

        LayoutUpdated -= OnLayoutUpdated;
        HookScrollViewer();
    }

    private void HookScrollViewer()
    {
        var scrollViewer = ScrollViewer;

        if (scrollViewer is null || ReferenceEquals(scrollViewer, hookedScrollViewer))
            return;

        if (hookedScrollViewer is not null)
            hookedScrollViewer.ScrollChanged -= OnScrollChanged;

        hookedScrollViewer               =  scrollViewer;
        hookedScrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged
    (
        object?                sender,
        ScrollChangedEventArgs e
    )
    {
        var scrollViewer = hookedScrollViewer;

        if (scrollViewer is null)
            return;

        // 只有纯粹的偏移变化才代表用户滚动；布局变化引起的偏移夹取不应改变跟随状态。
        var layoutChanged = e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0;

        if (e.OffsetDelta.Y != 0 && !layoutChanged && !suppressFollowUpdate)
        {
            var maximum = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);

            isFollowingTail = maximum - scrollViewer.Offset.Y <= 1;
        }

        // 内容高度与视口高度的变化都会改变底部位置，位于底部时都需要重新贴回。
        // 视口高度变化来自生成结束时管线阶段面板收起等布局调整，同样会夹取既有偏移。
        if (layoutChanged && isFollowingTail)
            ScheduleTailScroll();
    }

    /// <summary>
    /// 用户向上滚动即刻退出底部跟随，避免后续布局变化把用户强行拉回末尾。
    /// </summary>
    private void OnPointerWheelChanged
    (
        object?               sender,
        PointerWheelEventArgs e
    )
    {
        if (e.Delta.Y > 0)
            isFollowingTail = false;
    }

    /// <summary>
    /// 生成结束时管线阶段面板收起、输入区状态变化都会改变本控件高度，视口随之变化，
    /// 此时同样需要重新贴回底部。
    /// </summary>
    private void OnSizeChanged
    (
        object?              sender,
        SizeChangedEventArgs e
    )
    {
        if (isFollowingTail)
            ScheduleTailScroll();
    }

    private void OnStreamingEntryChanged()
    {
        HasStreamingEntry = StreamingEntry is not null;

        tailRetryCount = 0;

        ScheduleTailScroll();
    }

    /// <summary>
    /// 流式文本增长与条目并入稳定列表都会改变内容高度，合并到同一轮布局之后贴回底部。
    /// </summary>
    private void ScheduleTailScroll()
    {
        if (tailScrollScheduled)
            return;

        tailScrollScheduled = true;

        Dispatcher.UIThread.Post(ScrollToTail, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 贴底不使用滚动范围推算，而是以末尾项容器的实际位置对齐视口底部；
    /// 末尾项尚未实现时先把它滚动进视口，待容器出现后在下一轮布局校准。
    /// </summary>
    private void ScrollToTail()
    {
        tailScrollScheduled = false;

        if (!isFollowingTail)
            return;

        var scrollViewer = ScrollViewer;

        if (scrollViewer is null)
            return;

        var lastEntry = StreamingEntry ?? StableEntries?.OfType<DialogEntryViewModel>().LastOrDefault();

        if (lastEntry is null)
            return;

        var container = ContainerFromItem(lastEntry);

        if (container is null)
        {
            if (tailRetryCount++ >= 6)
                return;

            suppressFollowUpdate = true;
            scrollViewer.ScrollToEnd();
            suppressFollowUpdate = false;
            ScheduleTailScroll();
            return;
        }

        var bottom = container.TranslatePoint(new Point(0, container.Bounds.Height), scrollViewer)?.Y;

        if (bottom is null)
            return;

        var maximum = Math.Max(0, scrollViewer.Extent.Height          - scrollViewer.Viewport.Height);
        var target  = Math.Clamp(scrollViewer.Offset.Y + bottom.Value - scrollViewer.Viewport.Height, 0, maximum);

        tailRetryCount = 0;

        if (Math.Abs(target - scrollViewer.Offset.Y) <= 0.5)
            return;

        suppressFollowUpdate = true;
        scrollViewer.Offset  = scrollViewer.Offset.WithY(target);
        suppressFollowUpdate = false;
    }
}
