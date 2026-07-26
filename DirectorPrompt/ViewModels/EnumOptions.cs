using System.ComponentModel;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed class EnumOptions : INotifyPropertyChanged
{
    public static EnumOptions Instance { get; } = new();

    private EnumOptions() =>
        Loc.Instance.PropertyChanged += OnLanguageChanged;

    public IReadOnlyList<EnumOption<StateValueType>> ValueTypes =>
    [
        new(StateValueType.Numeric, Loc.Get("State.ValueType.Numeric")),
        new(StateValueType.Enum, Loc.Get("State.ValueType.Enum")),
        new(StateValueType.Text, Loc.Get("State.ValueType.Text"))
    ];

    public IReadOnlyList<EnumOption<Driver>> Drivers =>
    [
        new(Driver.Narrative, Loc.Get("State.Driver.Narrative")),
        new(Driver.System, Loc.Get("State.Driver.System"))
    ];

    public IReadOnlyList<EnumOption<SystemTrigger>> SystemTriggers =>
    [
        new(SystemTrigger.UserInput, Loc.Get("State.Trigger.UserInput")),
        new(SystemTrigger.RoundStart, Loc.Get("State.Trigger.RoundStart")),
        new(SystemTrigger.SceneChange, Loc.Get("State.Trigger.SceneChange")),
        new(SystemTrigger.NarrativeCompleted, Loc.Get("State.Trigger.NarrativeCompleted")),
        new(SystemTrigger.RoundEnd, Loc.Get("State.Trigger.RoundEnd"))
    ];

    public IReadOnlyList<EnumOption<StateRuleConditionSource>> RuleConditionSources =>
    [
        new(StateRuleConditionSource.CurrentValue, Loc.Get("State.Rule.Source.CurrentValue")),
        new(StateRuleConditionSource.GlobalState, Loc.Get("State.Rule.Source.GlobalState")),
        new(StateRuleConditionSource.CharacterState, Loc.Get("State.Rule.Source.CharacterState")),
        new(StateRuleConditionSource.InputContent, Loc.Get("State.Rule.Source.InputContent")),
        new(StateRuleConditionSource.InputType, Loc.Get("State.Rule.Source.InputType"))
    ];

    public IReadOnlyList<EnumOption<StateRuleComparison>> RuleComparisons =>
    [
        new(StateRuleComparison.Equals, Loc.Get("State.Rule.Comparison.Equals")),
        new(StateRuleComparison.NotEquals, Loc.Get("State.Rule.Comparison.NotEquals")),
        new(StateRuleComparison.GreaterThan, Loc.Get("State.Rule.Comparison.GreaterThan")),
        new(StateRuleComparison.GreaterThanOrEqual, Loc.Get("State.Rule.Comparison.GreaterThanOrEqual")),
        new(StateRuleComparison.LessThan, Loc.Get("State.Rule.Comparison.LessThan")),
        new(StateRuleComparison.LessThanOrEqual, Loc.Get("State.Rule.Comparison.LessThanOrEqual")),
        new(StateRuleComparison.Contains, Loc.Get("State.Rule.Comparison.Contains")),
        new(StateRuleComparison.Expression, Loc.Get("State.Rule.Comparison.Expression"))
    ];

    public IReadOnlyList<EnumOption<StateRuleConditionMatch>> RuleConditionMatches =>
    [
        new(StateRuleConditionMatch.All, Loc.Get("State.Rule.Match.All")),
        new(StateRuleConditionMatch.Any, Loc.Get("State.Rule.Match.Any"))
    ];

    public IReadOnlyList<EnumOption<NumericStateOperation>> NumericOperations =>
    [
        new(NumericStateOperation.Add, Loc.Get("State.Rule.Operation.Add")),
        new(NumericStateOperation.Set, Loc.Get("State.Rule.Operation.Set")),
        new(NumericStateOperation.Multiply, Loc.Get("State.Rule.Operation.Multiply")),
        new(NumericStateOperation.Expression, Loc.Get("State.Rule.Operation.Expression"))
    ];

    public IReadOnlyList<EnumOption<StateRuleRepeatPolicy>> RuleRepeatPolicies =>
    [
        new(StateRuleRepeatPolicy.EveryEvent, Loc.Get("State.Rule.Repeat.EveryEvent")),
        new(StateRuleRepeatPolicy.OnConditionEnter, Loc.Get("State.Rule.Repeat.OnConditionEnter")),
        new(StateRuleRepeatPolicy.OncePerRound, Loc.Get("State.Rule.Repeat.OncePerRound")),
        new(StateRuleRepeatPolicy.OncePerScene, Loc.Get("State.Rule.Repeat.OncePerScene")),
        new(StateRuleRepeatPolicy.OncePerSession, Loc.Get("State.Rule.Repeat.OncePerSession"))
    ];

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public event PropertyChangedEventHandler? PropertyChanged;

    public sealed record EnumOption<T>
    (
        T      Value,
        string Display
    ) where T : struct, Enum;
}
