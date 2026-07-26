using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;
using System.Collections.ObjectModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class NumericStateChangeRuleEditViewModel : ObservableObject
{
    private IReadOnlyList<string> expressionReferences = ["val"];

    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    public partial string Remarks { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SystemTrigger Trigger { get; set; } = SystemTrigger.RoundEnd;

    [ObservableProperty]
    public partial StateRuleConditionMatch ConditionMatch { get; set; } = StateRuleConditionMatch.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValuePlaceholder))]
    public partial NumericStateOperation? Operation { get; set; } = NumericStateOperation.Add;

    [ObservableProperty]
    public partial string ValueExpression { get; set; } = "1";

    [ObservableProperty]
    public partial bool IsExpressionValid { get; set; }

    [ObservableProperty]
    public partial string ExpressionValidationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial StateRuleRepeatPolicy? RepeatPolicy { get; set; } = StateRuleRepeatPolicy.EveryEvent;

    [ObservableProperty]
    public partial int Priority { get; set; }

    public ObservableCollection<StateRuleConditionEditViewModel> Conditions { get; } = [];

    public string Header => string.IsNullOrWhiteSpace(Remarks) ? Loc.Get("State.Rule.Untitled") : Remarks;

    public string ValuePlaceholder => Operation switch
    {
        NumericStateOperation.Add        => Loc.Get("State.Rule.Value.Add"),
        NumericStateOperation.Set        => Loc.Get("State.Rule.Value.Set"),
        NumericStateOperation.Multiply   => Loc.Get("State.Rule.Value.Multiply"),
        NumericStateOperation.Expression => Loc.Get("State.Rule.Value.Expression"),
        _                                => Loc.Get("State.Rule.ValueExpression")
    };

    public string ExpressionReferencesText => Loc.Get
    (
        "State.Rule.ExpressionReferences",
        string.Join(" / ", expressionReferences.Select(reference => $"{{{reference}}}"))
    );

    public void SetExpressionReferences(IEnumerable<string> references)
    {
        expressionReferences = references.Prepend("val").Distinct().ToList();
        OnPropertyChanged(nameof(ExpressionReferencesText));

        foreach (var condition in Conditions)
            condition.SetExpressionReferences(expressionReferences);

        ValidateExpression();
    }

    partial void OnValueExpressionChanged(string value) =>
        ValidateExpression();

    partial void OnOperationChanged(NumericStateOperation? value) =>
        ValidateExpression();

    private void ValidateExpression()
    {
        var result = StateRuleExpressionValidator.ValidateNumeric(ValueExpression, expressionReferences);
        IsExpressionValid         = result.IsValid;
        ExpressionValidationMessage = result.Message;
    }

    [RelayCommand]
    private void AddCondition()
    {
        var condition = new StateRuleConditionEditViewModel
        {
            Source = Trigger == SystemTrigger.UserInput ?
                         StateRuleConditionSource.InputContent :
                         StateRuleConditionSource.CurrentValue
        };
        condition.SetExpressionReferences(expressionReferences);
        Conditions.Add(condition);
    }

    [RelayCommand]
    private void DeleteCondition(StateRuleConditionEditViewModel? condition)
    {
        if (condition is not null)
            Conditions.Remove(condition);
    }
}
