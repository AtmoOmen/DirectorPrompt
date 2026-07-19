using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.ViewModels;

public sealed partial class EnumTransitionEditViewModel : ObservableObject
{
    public string Option { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChangeRules { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRandom))]
    [NotifyPropertyChangedFor(nameof(IsExpression))]
    [NotifyPropertyChangedFor(nameof(IsAlways))]
    [NotifyPropertyChangedFor(nameof(IsOnce))]
    public partial EnumTransitionMethod Method { get; set; } = EnumTransitionMethod.Random;

    [ObservableProperty]
    public partial float Weight { get; set; } = 1f;

    [ObservableProperty]
    public partial string? AttributeName { get; set; }

    [ObservableProperty]
    public partial string? Expression { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlways))]
    [NotifyPropertyChangedFor(nameof(IsOnce))]
    public partial EnumSwitchMode SwitchMode { get; set; } = EnumSwitchMode.Always;

    public bool IsRandom => Method == EnumTransitionMethod.Random;

    public bool IsExpression => Method == EnumTransitionMethod.Expression;

    public bool IsAlways => IsExpression && SwitchMode == EnumSwitchMode.Always;

    public bool IsOnce => IsExpression && SwitchMode == EnumSwitchMode.Once;
}
