using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;
using Markdig.Syntax;

namespace DirectorPrompt.ViewModels;

public sealed partial class DirectorContentBlockViewModel : ObservableObject
{
    public DirectiveType Type { get; init; }

    public string Content { get; init; } = string.Empty;

    [ObservableProperty]
    private MarkdownDocument? markdownDocument;

    public string TypeDisplay => Type switch
    {
        DirectiveType.Plot        => Loc.Get("Directive.Type.Plot"),
        DirectiveType.Constraint  => Loc.Get("Directive.Type.Constraint"),
        DirectiveType.SceneChange => Loc.Get("Directive.Type.SceneChange"),
        _                         => Type.ToString()
    };
}
