using Avalonia.Headless.XUnit;
using DirectorPrompt.Views.Components;

namespace DirectorPrompt.Tests;

public sealed class LiveMarkdownViewTests
{
    [AvaloniaFact]
    public void MarkdownSupportsAppendReplaceAndClear()
    {
        var view = new LiveMarkdownView();

        view.Markdown = "# Title";
        Assert.False(view.IsRenderCurrent);

        view.UpdateMarkdown();
        Assert.Equal("# Title", view.MarkdownBuilder.ToString());
        Assert.True(view.IsRenderCurrent);

        view.Markdown = "# Title\n\nBody";
        view.UpdateMarkdown();
        Assert.Equal("# Title\n\nBody", view.MarkdownBuilder.ToString());

        view.Markdown = "Replacement";
        view.UpdateMarkdown();
        Assert.Equal("Replacement", view.MarkdownBuilder.ToString());

        view.Markdown = string.Empty;
        view.UpdateMarkdown();
        Assert.Equal(string.Empty, view.MarkdownBuilder.ToString());
    }
}
