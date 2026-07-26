using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;
using System.Collections.ObjectModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class EnumTransitionEditViewModel : ObservableObject
{
    private IReadOnlyList<string> expressionReferences = ["val"];

    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    public string Option { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    public partial string Remarks { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChangeRules { get; set; } = string.Empty;

    [ObservableProperty]
    public partial float Weight { get; set; } = 1f;

    [ObservableProperty]
    public partial SystemTrigger? Trigger { get; set; }

    [ObservableProperty]
    public partial StateRuleConditionMatch ConditionMatch { get; set; } = StateRuleConditionMatch.All;

    [ObservableProperty]
    public partial StateRuleRepeatPolicy? RepeatPolicy { get; set; } = StateRuleRepeatPolicy.EveryEvent;

    [ObservableProperty]
    public partial int Priority { get; set; }

    public ObservableCollection<StateRuleConditionEditViewModel> Conditions { get; } = [];

    public string Header => string.IsNullOrWhiteSpace(Remarks) ?
                                $"{Loc.Get("State.Rule.SwitchTo")} {Option}" :
                                Remarks;

    public void SetExpressionReferences(IEnumerable<string> references)
    {
        expressionReferences = references.Prepend("val").Distinct().ToList();

        foreach (var condition in Conditions)
            condition.SetExpressionReferences(expressionReferences);
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
