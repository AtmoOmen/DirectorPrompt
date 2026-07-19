using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.ViewModels;

public sealed partial class NumericStateChangeRuleEditViewModel : ObservableObject
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    public partial string Remarks { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AttributeName { get; set; }

    [ObservableProperty]
    public partial string Expression { get; set; } = "true == true";

    [ObservableProperty]
    public partial string ChangeExpression { get; set; } = "{val} += 0";

    [ObservableProperty]
    public partial SystemTrigger Trigger { get; set; } = SystemTrigger.RoundEnd;

    [ObservableProperty]
    public partial EnumSwitchMode SwitchMode { get; set; } = EnumSwitchMode.Always;
}
