namespace DirectorPrompt.Agents;

public sealed class DirectiveEventData
{
    public string Type { get; set; } = "Plot";

    public string Content { get; set; } = string.Empty;

    public bool IsSystem { get; set; }
}
