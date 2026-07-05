using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.ViewModels;

public sealed partial class StateAttributeEditViewModel : ObservableObject
{
    public long ID { get; set; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNumericConfig))]
    [NotifyPropertyChangedFor(nameof(IsEnumConfig))]
    [NotifyPropertyChangedFor(nameof(IsCompositeConfig))]
    private StateValueType valueType = StateValueType.Numeric;

    [ObservableProperty]
    private Driver driver = Driver.Narrative;

    [ObservableProperty]
    private string currentValue = string.Empty;

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private float? minValue;

    [ObservableProperty]
    private float? maxValue;

    [ObservableProperty]
    private string unit = string.Empty;

    [ObservableProperty]
    private string changeRules = string.Empty;

    [ObservableProperty]
    private string options = string.Empty;

    [ObservableProperty]
    private SystemTrigger trigger = SystemTrigger.SceneChange;

    [ObservableProperty]
    private string generationGuide = string.Empty;

    [ObservableProperty]
    private SystemTrigger regenerateTrigger = SystemTrigger.SceneChange;

    public bool IsNumericConfig => ValueType == StateValueType.Numeric;

    public bool IsEnumConfig => ValueType == StateValueType.Enum;

    public bool IsCompositeConfig => ValueType == StateValueType.Composite;

    public string BuildConfig()
    {
        return (ValueType, Driver) switch
        {
            (StateValueType.Numeric, Driver.Narrative) => JsonSerializer.Serialize(new
            {
                min = MinValue,
                max = MaxValue,
                unit = Unit,
                changeRules = ChangeRules
            }),
            (StateValueType.Enum, Driver.System) => JsonSerializer.Serialize(new
            {
                options = Options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                trigger = Trigger.ToString(),
                transitionRules = new { },
                effects = new { }
            }),
            (StateValueType.Composite, Driver.System) => JsonSerializer.Serialize(new
            {
                generationGuide = GenerationGuide,
                regenerateTrigger = RegenerateTrigger.ToString(),
                regenerateCondition = (string?)null,
                itemCompleteEffect = new { },
                itemFailEffect = new { }
            }),
            _ => "{}"
        };
    }
}
