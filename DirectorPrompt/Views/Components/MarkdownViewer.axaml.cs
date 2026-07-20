using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LiveMarkdown.Avalonia;
using Markdig.Syntax;

namespace DirectorPrompt.Views.Components;

public sealed partial class MarkdownViewer : UserControl
{
    public static readonly StyledProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownViewer, string>(nameof(Markdown), string.Empty);

    public static readonly StyledProperty<bool> IsStreamingProperty =
        AvaloniaProperty.Register<MarkdownViewer, bool>(nameof(IsStreaming));

    public static readonly StyledProperty<MarkdownDocument?> MarkdownDocumentProperty =
        AvaloniaProperty.Register<MarkdownViewer, MarkdownDocument?>(nameof(MarkdownDocument));

    private          string           renderedMarkdown = string.Empty;
    private          string           queuedMarkdown   = string.Empty;
    private          bool             updateScheduled;
    private          IDisposable?     fallbackRelease;
    private readonly TextBlock        fallbackText;
    private readonly MarkdownRenderer renderer;

    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public bool IsStreaming
    {
        get => GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    public MarkdownDocument? MarkdownDocument
    {
        get => GetValue(MarkdownDocumentProperty);
        set => SetValue(MarkdownDocumentProperty, value);
    }

    internal ObservableStringBuilder MarkdownBuilder { get; } = new();

    internal bool IsRenderCurrent =>
        string.Equals(Markdown ?? string.Empty, renderedMarkdown, StringComparison.Ordinal);

    static MarkdownViewer()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownViewer>(static (view,         _) => view.RefreshMarkdown());
        IsStreamingProperty.Changed.AddClassHandler<MarkdownViewer>(static (view,      _) => view.RefreshMarkdown());
        MarkdownDocumentProperty.Changed.AddClassHandler<MarkdownViewer>(static (view, _) => view.RefreshMarkdown());
    }

    public MarkdownViewer()
    {
        AvaloniaXamlLoader.Load(this);
        fallbackText             =  this.FindControl<TextBlock>(nameof(FallbackText));
        renderer                 =  this.FindControl<MarkdownRenderer>(nameof(Renderer));
        renderer.MarkdownBuilder =  MarkdownBuilder;
        renderer.Rendered        += OnRendererRendered;
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        DetachedFromVisualTree   += OnDetachedFromVisualTree;
    }

    private void RefreshMarkdown()
    {
        if (MarkdownDocument is not null)
        {
            ShowMarkdownDocument();
            return;
        }

        ScheduleMarkdownUpdate();
    }

    private void ShowMarkdownDocument()
    {
        fallbackRelease?.Dispose();
        fallbackRelease           = null;
        fallbackText.Text         = string.Empty;
        fallbackText.IsVisible    = false;
        queuedMarkdown            = Markdown ?? string.Empty;
        renderedMarkdown          = queuedMarkdown;
        renderer.MarkdownDocument = MarkdownDocument;
        renderer.IsVisible        = true;
    }

    private void ScheduleMarkdownUpdate()
    {
        if (updateScheduled)
            return;

        updateScheduled = true;

        Dispatcher.UIThread.Post
        (
            () =>
            {
                updateScheduled = false;

                if (MarkdownDocument is not null)
                    return;

                UpdateMarkdown();
            },
            DispatcherPriority.Render
        );
    }

    internal void UpdateMarkdown()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Markdown updates must run on the UI thread");

        if (MarkdownDocument is not null)
            return;

        var markdown = Markdown ?? string.Empty;

        if (renderer.MarkdownDocument is not null)
        {
            renderer.MarkdownDocument = null;
            queuedMarkdown            = string.Empty;
            renderedMarkdown          = string.Empty;
        }

        if (markdown == queuedMarkdown)
            return;

        var append = IsStreaming && markdown.StartsWith(queuedMarkdown, StringComparison.Ordinal);

        if (!append || !renderer.IsVisible)
            ShowFallback(markdown);
        else
            fallbackText.Text = markdown;

        if (append)
            MarkdownBuilder.Append(markdown[queuedMarkdown.Length..]);
        else
            MarkdownBuilder.Set(markdown);

        queuedMarkdown   = markdown;
        renderedMarkdown = markdown;
    }

    private void ShowFallback(string markdown)
    {
        fallbackRelease?.Dispose();
        fallbackRelease        = null;
        fallbackText.Text      = markdown;
        fallbackText.IsVisible = true;
        fallbackText.Opacity   = 1;
        renderer.IsVisible     = false;
    }

    private void OnRendererRendered(object? sender, EventArgs e)
    {
        if (!string.Equals(MarkdownBuilder.ToString(), queuedMarkdown, StringComparison.Ordinal))
            return;

        fallbackText.Opacity = 0;
        renderer.IsVisible   = true;
        ScheduleFallbackRelease();
    }

    private void OnEffectiveViewportChanged(object? sender, EventArgs e)
    {
        if (renderer.IsVisible && fallbackText.IsVisible)
            ScheduleFallbackRelease();
    }

    private void ScheduleFallbackRelease()
    {
        fallbackRelease?.Dispose();
        fallbackRelease = DispatcherTimer.RunOnce
        (
            ReleaseFallbackLayout,
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background
        );
    }

    private void ReleaseFallbackLayout()
    {
        fallbackRelease = null;

        if (renderer.IsVisible && string.Equals(MarkdownBuilder.ToString(), queuedMarkdown, StringComparison.Ordinal))
            fallbackText.IsVisible = false;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        fallbackRelease?.Dispose();
        fallbackRelease = null;
    }
}
