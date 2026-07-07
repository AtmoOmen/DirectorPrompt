using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.ViewModels;

public sealed partial class AuditSettingViewModel : ObservableObject
{
    [ObservableProperty]
    public partial AuditMode Mode { get; set; } = AuditMode.Blocking;

    [ObservableProperty]
    public partial int MaxRetries { get; set; } = 2;
}
