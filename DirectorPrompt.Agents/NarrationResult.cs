namespace DirectorPrompt.Agents;

public record NarrationResult
(
    string                           Narrative,
    IReadOnlyList<NarrationActivity> Activities,
    long                             RoundID
);
