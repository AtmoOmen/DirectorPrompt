namespace DirectorPrompt.Agents;

public sealed record NarrationRecallEntry
(
    string  Content,
    double  Relevance,
    string? Remarks = null
);
