using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents;

public record DirectiveItem
(
    DirectiveType Type,
    string        Content,
    int           Order,
    int?          TTL = null,
    bool          IsSystem = false
);
