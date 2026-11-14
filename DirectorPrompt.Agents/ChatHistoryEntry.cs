using Microsoft.Extensions.AI;

namespace DirectorPrompt.Agents;

public record ChatHistoryEntry
(
    long                        RoundID,
    string                      DirectorInput,
    string                      NarrativeOutput,
    IReadOnlyList<ChatMessage>? Messages = null
);
