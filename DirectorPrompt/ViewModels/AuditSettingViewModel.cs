using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.ViewModels;

public sealed partial class AuditSettingViewModel : ObservableObject
{
    [ObservableProperty]
    private AuditMode mode = AuditMode.Blocking;

    [ObservableProperty]
    private int maxRetries = 2;
}
