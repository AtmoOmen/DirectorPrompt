using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents;

public sealed record DialogHistoryResult
(
    IReadOnlyList<DialogHistoryResult.RoundEntry> Rounds
)
{
    public sealed record RoundEntry
    (
        long                                                RoundID,
        long?                                               DirectorEventID,
        IReadOnlyList<(DirectiveType Type, string Content)> DirectorBlocks,
        long?                                               NarrativeEventID,
        string                                              NarrativeText
    );
}
