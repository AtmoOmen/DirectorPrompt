namespace DirectorPrompt.Agents;

public sealed record NarrationTrace
(
    IReadOnlyList<NarrationActivity>    Activities,
    IReadOnlyDictionary<string, string> Intents
);
