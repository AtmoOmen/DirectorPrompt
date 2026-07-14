namespace DirectorPrompt.Agents;

public record ChatHistoryEntry
(
    long   RoundID,
    string DirectorInput,
    string NarrativeOutput
);
