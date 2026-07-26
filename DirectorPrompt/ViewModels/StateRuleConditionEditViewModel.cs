using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed partial class StateRuleConditionEditViewModel : ObservableObject
{
    private IReadOnlyList<string> expressionReferences = ["val"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStateSource))]
    [NotifyPropertyChangedFor(nameof(IsGlobalStateSource))]
    [NotifyPropertyChangedFor(nameof(IsCharacterStateSource))]
    [NotifyPropertyChangedFor(nameof(ValuePlaceholder))]
    public partial StateRuleConditionSource Source { get; set; } = StateRuleConditionSource.CurrentValue;

    [ObservableProperty]
    public partial string? StateName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValuePlaceholder))]
    [NotifyPropertyChangedFor(nameof(IsExpressionComparison))]
    public partial StateRuleComparison Comparison { get; set; } = StateRuleComparison.Equals;

    [ObservableProperty]
    public partial string ExpectedValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpressionValid { get; set; }

    [ObservableProperty]
    public partial string ExpressionValidationMessage { get; set; } = string.Empty;

    public bool IsStateSource => Source is StateRuleConditionSource.GlobalState or StateRuleConditionSource.CharacterState;

    public bool IsGlobalStateSource => Source == StateRuleConditionSource.GlobalState;

    public bool IsCharacterStateSource => Source == StateRuleConditionSource.CharacterState;

    public bool IsExpressionComparison => Comparison == StateRuleComparison.Expression;

    public string ValuePlaceholder => Comparison == StateRuleComparison.Expression ?
                                          Loc.Get("State.Rule.ExpressionPlaceholder") :
                                          Source == StateRuleConditionSource.InputContent ?
                                              Loc.Get("State.Rule.InputContentPlaceholder") :
                                              Loc.Get("State.Rule.Value");

    public string ExpressionReferencesText => Loc.Get
    (
        "State.Rule.ExpressionReferences",
        string.Join(" / ", expressionReferences.Select(reference => $"{{{reference}}}"))
    );

    public void SetExpressionReferences(IEnumerable<string> references)
    {
        expressionReferences = references.Prepend("val").Distinct().ToList();
        OnPropertyChanged(nameof(ExpressionReferencesText));
        ValidateExpression();
    }

    partial void OnComparisonChanged(StateRuleComparison value) =>
        ValidateExpression();

    partial void OnExpectedValueChanged(string value) =>
        ValidateExpression();

    private void ValidateExpression()
    {
        if (!IsExpressionComparison)
        {
            IsExpressionValid           = false;
            ExpressionValidationMessage = string.Empty;
            return;
        }

        var result = StateRuleExpressionValidator.ValidateBoolean(ExpectedValue, expressionReferences);
        IsExpressionValid           = result.IsValid;
        ExpressionValidationMessage = result.Message;
    }
}
