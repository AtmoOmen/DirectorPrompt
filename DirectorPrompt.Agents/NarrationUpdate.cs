namespace DirectorPrompt.Agents;

public sealed record NarrationUpdate
(
    string?            Narrative = null,
    NarrationActivity? Activity  = null
);
