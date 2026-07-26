using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Models;

public sealed record StateRuleEvent
(
    SystemTrigger  Trigger,
    string         EventKey,
    long           RoundID,
    long?          SceneID,
    string?        InputContent,
    DirectiveType? InputType
);
