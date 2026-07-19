using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.ViewModels;

public sealed partial class StateAttributeEditViewModel : ObservableObject
{
    public long ID { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNumericConfig))]
    [NotifyPropertyChangedFor(nameof(IsEnumConfig))]
    [NotifyPropertyChangedFor(nameof(IsDriverVisible))]
    public partial StateValueType ValueType { get; set; } = StateValueType.Numeric;

    [ObservableProperty]
    public partial Driver Driver { get; set; } = Driver.Narrative;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCategoryScope))]
    public partial StateScope Scope { get; set; } = StateScope.Global;

    [ObservableProperty]
    public partial long? CategoryID { get; set; }

    public bool IsCategoryScope => Scope == StateScope.Category;

    [ObservableProperty]
    public partial string CurrentValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial float? MinValue { get; set; }

    [ObservableProperty]
    public partial float? MaxValue { get; set; }

    [ObservableProperty]
    public partial float? InitialValue { get; set; }

    [ObservableProperty]
    public partial string Unit { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChangeRules { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Options { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SystemTrigger Trigger { get; set; } = SystemTrigger.SceneChange;

    public ObservableCollection<PhaseEditViewModel> Phases { get; } = [];

    public ObservableCollection<EnumTransitionEditViewModel> Transitions { get; } = [];

    public ObservableCollection<string> AvailableNumericAttributes { get; } = [];

    public bool IsNumericConfig => ValueType == StateValueType.Numeric;

    public bool IsEnumConfig => ValueType == StateValueType.Enum;

    public bool IsDriverVisible => ValueType == StateValueType.Numeric;

    partial void OnOptionsChanged(string value) =>
        SyncTransitions();

    private void SyncTransitions()
    {
        var options = Options.Split
        (
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        var optionSet = new HashSet<string>(options);

        for (var i = Transitions.Count - 1; i >= 0; i--)
            if (!optionSet.Contains(Transitions[i].Option))
                Transitions.RemoveAt(i);

        var existing = Transitions.Select(t => t.Option).ToHashSet();

        foreach (var opt in options)
        {
            if (!existing.Contains(opt))
                Transitions.Add(new EnumTransitionEditViewModel { Option = opt });
        }
    }

    public string BuildConfig()
    {
        var phases = Phases.Select
        (p => new Phase
            {
                Name              = p.Name,
                Expression        = p.Expression,
                KnowledgeIDs      = p.GetKnowledgeIDs(),
                KnowledgeGroupIDs = p.GetKnowledgeGroupIDs(),
                EnterDirectives = p.EnterDirectiveInput.Directives.Select
                (d => new DirectiveConfig
                    {
                        Type    = d.Type,
                        Content = d.Content,
                        TTL     = d.TTL
                    }
                ).ToList(),
                ExitDirectives = p.ExitDirectiveInput.Directives.Select
                (d => new DirectiveConfig
                    {
                        Type    = d.Type,
                        Content = d.Content,
                        TTL     = d.TTL
                    }
                ).ToList()
            }
        ).ToList();

        var transitions = Transitions.Select
        (t => new EnumTransitionConfig
            {
                Option        = t.Option,
                Method        = t.Method,
                Weight        = t.Weight,
                AttributeName = t.AttributeName,
                Expression    = t.Expression,
                SwitchMode    = t.SwitchMode
            }
        ).ToList();

        var dto = new StateAttributeConfig
        {
            Min         = MinValue,
            Max         = MaxValue,
            Initial     = InitialValue,
            Unit        = Unit,
            ChangeRules = ChangeRules,
            Options     = Options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Trigger     = Trigger.ToString(),
            Transitions = transitions,
            Phases      = phases
        };

        return JsonSerializer.Serialize(dto, JsonOptions.Compact);
    }
}
