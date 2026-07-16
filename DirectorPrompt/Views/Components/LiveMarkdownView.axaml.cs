using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LiveMarkdown.Avalonia;

namespace DirectorPrompt.Views.Components;

public sealed partial class LiveMarkdownView : UserControl
{
    public static readonly StyledProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<LiveMarkdownView, string>(nameof(Markdown), string.Empty);

    private string renderedMarkdown = string.Empty;
    private bool   updateScheduled;

    private MarkdownRenderer RendererControl =>
        this.GetLogicalDescendants().OfType<MarkdownRenderer>().First(control => control.Name == "Renderer");

    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    internal ObservableStringBuilder MarkdownBuilder { get; } = new();

    internal bool IsRenderCurrent => string.Equals(Markdown ?? string.Empty, renderedMarkdown, StringComparison.Ordinal);

    static LiveMarkdownView() =>
        MarkdownProperty.Changed.AddClassHandler<LiveMarkdownView>(static (view, _) => view.ScheduleMarkdownUpdate());

    public LiveMarkdownView()
    {
        AvaloniaXamlLoader.Load(this);
        RendererControl.MarkdownBuilder = MarkdownBuilder;
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
                UpdateMarkdown();
            },
            DispatcherPriority.Background
        );
    }

    internal void UpdateMarkdown()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Markdown updates must run on the UI thread");

        var markdown = Markdown ?? string.Empty;

        if (markdown == renderedMarkdown)
            return;

        if (markdown.StartsWith(renderedMarkdown, StringComparison.Ordinal))
            MarkdownBuilder.Append(markdown[renderedMarkdown.Length..]);
        else
        {
            MarkdownBuilder.Clear();
            MarkdownBuilder.Append(markdown);
        }

        renderedMarkdown = markdown;
    }
}
