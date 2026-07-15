using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.ViewModels;

public sealed class MemorySettingViewModel : ObservableObject
{
    public MemoryConfig Config { get; }

    public MemorySettingViewModel(MemoryConfig config) => Config = config;

    public int RecallTopK
    {
        get => Config.RecallTopK;
        set
        {
            if (Config.RecallTopK != value)
            {
                Config.RecallTopK = value;
                OnPropertyChanged();
            }
        }
    }

    public int TokenBudget
    {
        get => Config.TokenBudget;
        set
        {
            if (Config.TokenBudget != value)
            {
                Config.TokenBudget = value;
                OnPropertyChanged();
            }
        }
    }

    public float MinRelevance
    {
        get => Config.MinRelevance;
        set
        {
            if (Config.MinRelevance != value)
            {
                Config.MinRelevance = value;
                OnPropertyChanged();
            }
        }
    }

    public float TimeDecayLambda
    {
        get => Config.TimeDecayLambda;
        set
        {
            if (Config.TimeDecayLambda != value)
            {
                Config.TimeDecayLambda = value;
                OnPropertyChanged();
            }
        }
    }
}
