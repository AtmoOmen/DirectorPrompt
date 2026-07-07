using System.ComponentModel;
using System.Windows.Documents;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Markdown;

namespace DirectorPrompt.ViewModels;

public sealed class DirectorContentBlockViewModel : INotifyPropertyChanged
{
    private FlowDocument? document;

    public DirectiveType Type { get; init; }

    public string Content { get; init; } = string.Empty;

    public string TypeDisplay => Type switch
    {
        DirectiveType.Plot                => "剧情",
        DirectiveType.Tone                => "基调",
        DirectiveType.TemporaryConstraint => "临时约束",
        DirectiveType.SceneChange         => "时间/场景变更",
        _                                 => Type.ToString()
    };

    public FlowDocument? Document
    {
        get => document;
        private set
        {
            document = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Document)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RenderMarkdown()
    {
        Document = MarkdownRenderer.Render(Content);
    }
}
