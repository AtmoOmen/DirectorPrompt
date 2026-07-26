using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Configurations;

public static class StateRuleConfigMigration
{
    public static StateAttributeConfig Normalize(StateAttributeConfig config, StateScope scope)
    {
        var transitions = config.Transitions ?? [];
        var legacyPriorities = transitions
                               .Select((rule, index) => new { Rule = rule, Index = index })
                               .Where(item => item.Rule.Method == EnumTransitionMethod.Expression)
                               .OrderBy(item => item.Rule.Weight)
                               .ThenByDescending(item => item.Index)
                               .Select((item, priority) => new { item.Index, Priority = priority + 1 })
                               .ToDictionary(item => item.Index, item => item.Priority);

        return config with
        {
            NumericChanges = config.NumericChanges.Select(rule => Normalize(rule, scope)).ToList(),
            Transitions = config.Transitions?.Select
            (
                (rule, index) => Normalize
                (
                    rule,
                    config.Trigger,
                    scope,
                    legacyPriorities.GetValueOrDefault(index)
                )
            ).ToList()
        };
    }

    private static NumericStateChangeRuleConfig Normalize
    (
        NumericStateChangeRuleConfig rule,
        StateScope                   scope
    )
    {
        var conditions = rule.Conditions.ToList();

        if (conditions.Count == 0 &&
            !string.IsNullOrWhiteSpace(rule.Expression) &&
            !IsUnconditional(rule.Expression))
        {
            conditions.Add(CreateExpressionCondition(rule.AttributeName, rule.Expression, scope));
        }

        return new NumericStateChangeRuleConfig
        {
            ID              = rule.ID,
            Remarks         = rule.Remarks,
            Trigger         = rule.Trigger,
            Conditions      = conditions,
            ConditionMatch  = rule.ConditionMatch,
            Operation       = rule.Operation ?? NumericStateOperation.Expression,
            ValueExpression = rule.ValueExpression ?? rule.ChangeExpression ?? "{val}",
            RepeatPolicy = rule.RepeatPolicy ??
                           (rule.SwitchMode == EnumSwitchMode.Once ?
                                StateRuleRepeatPolicy.OnConditionEnter :
                                StateRuleRepeatPolicy.EveryEvent),
            Priority = rule.Priority
        };
    }

    private static EnumTransitionConfig Normalize
    (
        EnumTransitionConfig rule,
        string?              attributeTrigger,
        StateScope           scope,
        int                  legacyPriority
    )
    {
        var conditions = rule.Conditions.ToList();

        if (conditions.Count == 0 &&
            rule.Method == EnumTransitionMethod.Expression &&
            !string.IsNullOrWhiteSpace(rule.Expression))
        {
            conditions.Add(CreateExpressionCondition(rule.AttributeName, rule.Expression, scope));
        }

        return new EnumTransitionConfig
        {
            ID             = rule.ID,
            Option         = rule.Option,
            Remarks        = rule.Remarks,
            ChangeRules    = rule.ChangeRules,
            Weight         = rule.Weight,
            Trigger        = rule.Trigger ?? ParseTrigger(attributeTrigger),
            Conditions     = conditions,
            ConditionMatch = rule.ConditionMatch,
            RepeatPolicy = rule.RepeatPolicy ??
                           (rule.SwitchMode == EnumSwitchMode.Once ?
                                StateRuleRepeatPolicy.OnConditionEnter :
                                StateRuleRepeatPolicy.EveryEvent),
            Priority = rule.Method == EnumTransitionMethod.Expression && rule.Priority == 0 ?
                           legacyPriority :
                           rule.Priority
        };
    }

    private static StateRuleConditionConfig CreateExpressionCondition
    (
        string?    stateName,
        string     expression,
        StateScope scope
    ) =>
        new()
        {
            Source = string.IsNullOrWhiteSpace(stateName) ?
                         StateRuleConditionSource.CurrentValue :
                         scope == StateScope.Global ?
                             StateRuleConditionSource.GlobalState :
                             StateRuleConditionSource.CharacterState,
            StateName     = string.IsNullOrWhiteSpace(stateName) ? null : stateName,
            Comparison    = StateRuleComparison.Expression,
            ExpectedValue = expression
        };

    private static bool IsUnconditional(string expression) =>
        string.Equals(expression.Replace(" ", string.Empty), "true==true", StringComparison.OrdinalIgnoreCase);

    private static SystemTrigger ParseTrigger(string? value) =>
        Enum.TryParse<SystemTrigger>(value, true, out var trigger) && Enum.IsDefined(trigger) ?
            trigger :
            SystemTrigger.SceneChange;
}
