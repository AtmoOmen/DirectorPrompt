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
        renderer                 =  this.FindControl<MarkdownRenderer>(nameof(Renderer));
        renderer.MarkdownBuilder =  MarkdownBuilder;
        DataContextChanged       += OnDataContextChanged;
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
        queuedMarkdown            = Markdown ?? string.Empty;
        renderedMarkdown          = queuedMarkdown;
        renderer.MarkdownDocument = MarkdownDocument;
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
                    ShowMarkdownDocument();
                else
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
            MarkdownBuilder.Clear();
            queuedMarkdown   = string.Empty;
            renderedMarkdown = string.Empty;
        }

        if (markdown == queuedMarkdown)
            return;

        var append = IsStreaming && markdown.StartsWith(queuedMarkdown, StringComparison.Ordinal);

        if (append)
            MarkdownBuilder.Append(markdown[queuedMarkdown.Length..]);
        else
            MarkdownBuilder.Set(markdown);

        queuedMarkdown   = markdown;
        renderedMarkdown = markdown;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        renderer.MarkdownBuilder  = null;
        renderer.MarkdownDocument = null;
        MarkdownBuilder.Clear();
        queuedMarkdown           = string.Empty;
        renderedMarkdown         = string.Empty;
        renderer.MarkdownBuilder = MarkdownBuilder;
        RefreshMarkdown();
    }
}
