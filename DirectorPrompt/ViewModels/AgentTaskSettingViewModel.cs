using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed partial class AgentTaskSettingViewModel : ObservableObject
{
    [ObservableProperty]
    public partial AgentTaskType TaskType { get; set; }

    [ObservableProperty]
    public partial string ModelConfigID { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? PromptID { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    public static AgentTaskType[] AllTaskTypes { get; } = Enum.GetValues<AgentTaskType>();

    public string TaskTypeDisplay => Loc.Get($"Agent.Task.{TaskType}");

    public string PromptDisplay =>
        string.IsNullOrEmpty(PromptID) ?
            Loc.Get("Settings.Task.PromptBuiltIn") :
            Loc.Get("Settings.Task.PromptCustom");
}
