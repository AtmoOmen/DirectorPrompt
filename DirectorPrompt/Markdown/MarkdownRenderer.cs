using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;

namespace DirectorPrompt.Markdown;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static FlowDocument Render(string markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
            PagePadding = new Thickness(0)
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return document;
        }

        var parsed = Markdig.Markdown.Parse(markdown, Pipeline);
        var renderer = new FlowDocumentRenderer();
        renderer.Render(document, parsed);

        return document;
    }
}
