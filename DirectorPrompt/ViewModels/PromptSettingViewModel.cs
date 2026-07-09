using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class PromptSettingViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ID { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;
}
