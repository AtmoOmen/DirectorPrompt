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
        new(StateValueType.Enum, Loc.Get("State.ValueType.Enum"))
    ];

    public IReadOnlyList<EnumOption<Driver>> Drivers =>
    [
        new(Driver.Narrative, Loc.Get("State.Driver.Narrative")),
        new(Driver.System, Loc.Get("State.Driver.System"))
    ];

    public IReadOnlyList<EnumOption<SystemTrigger>> SystemTriggers =>
    [
        new(SystemTrigger.SceneChange, Loc.Get("State.Trigger.SceneChange")),
        new(SystemTrigger.RoundEnd, Loc.Get("State.Trigger.RoundEnd"))
    ];

    public IReadOnlyList<EnumOption<EnumTransitionMethod>> TransitionMethods =>
    [
        new(EnumTransitionMethod.Random, Loc.Get("State.TransitionMethod.Random")),
        new(EnumTransitionMethod.Expression, Loc.Get("State.TransitionMethod.Expression"))
    ];

    public IReadOnlyList<EnumOption<EnumSwitchMode>> SwitchModes =>
    [
        new(EnumSwitchMode.Always, Loc.Get("State.SwitchMode.Always")),
        new(EnumSwitchMode.Once, Loc.Get("State.SwitchMode.Once"))
    ];

    public IReadOnlyList<EnumOption<EnumSwitchMode>> NumericChangeSwitchModes =>
    [
        new(EnumSwitchMode.Always, Loc.Get("State.NumericChange.Always")),
        new(EnumSwitchMode.Once, Loc.Get("State.NumericChange.Once"))
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
