using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.ViewModels;

public sealed class PromptSettingViewModel : ObservableObject
{
    public PromptConfig Config { get; }

    public PromptSettingViewModel(PromptConfig config) => Config = config;

    public string ID => Config.ID;

    public string DisplayName
    {
        get => Config.DisplayName;
        set
        {
            if (Config.DisplayName != value)
            {
                Config.DisplayName = value;
                OnPropertyChanged();
            }
        }
    }

    public string Content
    {
        get => Config.Content;
        set
        {
            if (Config.Content != value)
            {
                Config.Content = value;
                OnPropertyChanged();
            }
        }
    }
}
