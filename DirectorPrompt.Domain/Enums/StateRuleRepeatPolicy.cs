namespace DirectorPrompt.Domain.Enums;

public enum StateRuleRepeatPolicy
{
    EveryEvent,

    OnConditionEnter,

    OncePerRound,

    OncePerScene,

    OncePerSession
}
