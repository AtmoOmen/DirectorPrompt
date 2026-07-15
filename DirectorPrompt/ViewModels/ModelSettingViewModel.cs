using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.ViewModels;

public sealed partial class ModelSettingViewModel : ObservableObject
{
    public ModelConfig Config { get; }

    public ModelSettingViewModel(ModelConfig config) => Config = config;

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

    public string ProviderID
    {
        get => Config.ProviderID;
        set
        {
            if (Config.ProviderID != value)
            {
                Config.ProviderID = value;
                OnPropertyChanged();
            }
        }
    }

    public string ModelName
    {
        get => Config.ModelName;
        set
        {
            if (Config.ModelName != value)
            {
                Config.ModelName = value;
                OnPropertyChanged();
            }
        }
    }

    public float Temperature
    {
        get => Config.Temperature;
        set
        {
            if (Config.Temperature != value)
            {
                Config.Temperature = value;
                OnPropertyChanged();
            }
        }
    }

    public string ReasoningEffort
    {
        get => Config.ReasoningEffort ?? string.Empty;
        set
        {
            if (Config.ReasoningEffort != value)
            {
                Config.ReasoningEffort = value;
                OnPropertyChanged();
            }
        }
    }

    public string ExtraParameters
    {
        get => Config.ExtraParameters ?? string.Empty;
        set
        {
            if (Config.ExtraParameters != value)
            {
                Config.ExtraParameters = value;
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

    [ObservableProperty]
    public partial bool IsFetchingModels { get; set; }

    [ObservableProperty]
    public partial bool IsTestingConnection { get; set; }

    [ObservableProperty]
    public partial string ModelFetchMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConnectionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool? ConnectionSuccess { get; set; }

    public ObservableCollection<string> AvailableModels { get; } = [];
}
