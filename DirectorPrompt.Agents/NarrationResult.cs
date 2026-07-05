using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Agents;

public record NarrationResult
(
    string                   Narrative,
    string                   Thinking,
    long                     RoundID,
    IReadOnlyList<Violation> Violations,
    bool                     AuditPassed
);
