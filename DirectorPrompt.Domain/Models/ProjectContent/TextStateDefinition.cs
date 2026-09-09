using System.ComponentModel;

namespace DirectorPrompt.Domain.Models;

public sealed class TextStateDefinition
{
    [Description("自然语言叙事生成指引")]
    public string? NarrativeGuidance { get; set; }
}
