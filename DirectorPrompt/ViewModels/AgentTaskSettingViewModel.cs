using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed class AgentTaskSettingViewModel : ObservableObject
{
    public AgentTaskConfig Config { get; }

    public AgentTaskSettingViewModel(AgentTaskConfig config) => Config = config;

    public AgentTaskType TaskType => Config.TaskType;

    public string TaskTypeDisplay => Loc.Get($"Agent.Task.{Config.TaskType}");

    public string ModelConfigID
    {
        get => Config.ModelConfigID;
        set
        {
            if (Config.ModelConfigID != value)
            {
                Config.ModelConfigID = value;
                OnPropertyChanged();
            }
        }
    }

    public string? PromptID
    {
        get => Config.PromptID;
        set
        {
            if (Config.PromptID != value)
            {
                Config.PromptID = value;
                OnPropertyChanged();
            }
        }
    }
}
