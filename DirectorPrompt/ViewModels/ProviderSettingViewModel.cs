using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.ViewModels;

public sealed class ProviderSettingViewModel : ObservableObject
{
    public ProviderConfig Config { get; }

    public ProviderSettingViewModel(ProviderConfig config) => Config = config;

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

    public string Provider
    {
        get => Config.Provider;
        set
        {
            if (Config.Provider != value)
            {
                Config.Provider = value;
                OnPropertyChanged();
            }
        }
    }

    public string Endpoint
    {
        get => Config.Endpoint;
        set
        {
            if (Config.Endpoint != value)
            {
                Config.Endpoint = value;
                OnPropertyChanged();
            }
        }
    }

    public string APIKey
    {
        get => Config.APIKey ?? string.Empty;
        set
        {
            if (Config.APIKey != value)
            {
                Config.APIKey = value;
                OnPropertyChanged();
            }
        }
    }

    public string CustomHeaders
    {
        get => Config.CustomHeaders ?? string.Empty;
        set
        {
            if (Config.CustomHeaders != value)
            {
                Config.CustomHeaders = value;
                OnPropertyChanged();
            }
        }
    }

    public static string[] AvailableProviders { get; } = ["openai", "ollama", "anthropic"];
}
