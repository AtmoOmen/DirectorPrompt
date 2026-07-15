using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.ViewModels;

public sealed class KnowledgeSettingViewModel : ObservableObject
{
    public KnowledgeRetrievalConfig Config { get; }

    public KnowledgeSettingViewModel(KnowledgeRetrievalConfig config) => Config = config;

    public int SemanticTopK
    {
        get => Config.SemanticTopK;
        set
        {
            if (Config.SemanticTopK != value)
            {
                Config.SemanticTopK = value;
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
}
