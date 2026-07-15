using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.ViewModels;

public sealed partial class EmbeddingSettingViewModel : ObservableObject
{
    public EmbeddingConfig Config { get; }

    public EmbeddingSettingViewModel(EmbeddingConfig config) => Config = config;

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

    [ObservableProperty]
    public partial bool IsFetchingModels { get; set; }

    [ObservableProperty]
    public partial string ModelFetchMessage { get; set; } = string.Empty;

    public ObservableCollection<string> AvailableModels { get; } = [];

    [ObservableProperty]
    public partial bool IsTestingConnection { get; set; }

    [ObservableProperty]
    public partial string ConnectionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool? ConnectionSuccess { get; set; }
}
