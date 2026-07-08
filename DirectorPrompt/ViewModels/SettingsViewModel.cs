using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Infrastructure.Extensions;
using DirectorPrompt.Localization;
using Serilog;

namespace DirectorPrompt.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IModelConnectionTester connectionTester;
    private readonly ILocalizationService   localizationService;
    private readonly UserSettings           userSettings;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string ValidationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; } = string.Empty;

    public ObservableCollection<AgentSettingViewModel> Agents { get; } = [];

    public EmbeddingSettingViewModel Embedding { get; } = new();

    public IReadOnlyDictionary<string, string> AvailableLanguages =>
        localizationService.AvailableLanguages;

    public SettingsViewModel
    (
        UserSettings           userSettings,
        IModelConnectionTester connectionTester,
        ILocalizationService   localizationService
    )
    {
        this.connectionTester    = connectionTester;
        this.localizationService = localizationService;
        this.userSettings        = userSettings;

        LoadSettings(userSettings);
    }

    private void LoadSettings(UserSettings userSettings)
    {
        SelectedLanguage = userSettings.Localization.Language;

        if (string.IsNullOrEmpty(SelectedLanguage))
            SelectedLanguage = localizationService.CurrentLanguage;

        LoadAgents(userSettings.Orchestrator.Agents);
        LoadEmbeddingConfig(userSettings.EmbeddingConfig);
    }

    private void LoadEmbeddingConfig(ModelConfig config)
    {
        Embedding.Provider  = config.Provider;
        Embedding.Endpoint  = config.Endpoint;
        Embedding.APIKey    = config.APIKey ?? string.Empty;
        Embedding.ModelName = config.ModelName;
    }

    private void LoadAgents(List<AgentDefinition> agents)
    {
        if (agents is not { Count: > 0 })
            return;

        foreach (var agent in agents)
        {
            Agents.Add
            (
                new()
                {
                    Role         = agent.Role,
                    Provider     = agent.ModelConfig.Provider,
                    Endpoint     = agent.ModelConfig.Endpoint,
                    APIKey       = agent.ModelConfig.APIKey ?? string.Empty,
                    ModelName    = agent.ModelConfig.ModelName,
                    SystemPrompt = agent.SystemPrompt,
                    Temperature  = agent.Temperature,
                    Tools        = agent.Tools
                }
            );
        }
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (string.IsNullOrEmpty(value) || !AvailableLanguages.ContainsKey(value))
            return;

        if (localizationService.CurrentLanguage != value)
            localizationService.LoadLanguage(value);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;

        try
        {
            userSettings.Orchestrator.Agents = Agents.Select
            (a => new AgentDefinition
                {
                    Role = a.Role,
                    ModelConfig = new ModelConfig
                    {
                        Provider  = a.Provider,
                        Endpoint  = a.Endpoint,
                        APIKey    = a.APIKey,
                        ModelName = a.ModelName
                    },
                    SystemPrompt = a.SystemPrompt,
                    Temperature  = a.Temperature,
                    Tools        = a.Tools
                }
            ).ToList();

            userSettings.EmbeddingConfig = new ModelConfig
            {
                Provider  = Embedding.Provider,
                Endpoint  = Embedding.Endpoint,
                APIKey    = Embedding.APIKey,
                ModelName = Embedding.ModelName
            };

            userSettings.Localization.Language = SelectedLanguage;

            await userSettings.SaveAsync();

            ValidationMessage = Loc.Get("Settings.Saved");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存设置失败");
            ValidationMessage = Loc.Get("Settings.SaveFailed", ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task TestAgentConnectionAsync(AgentSettingViewModel? agent)
    {
        if (agent is null)
            return;

        agent.IsTestingConnection = true;
        agent.ConnectionSuccess   = null;
        agent.ConnectionMessage   = Loc.Get("Settings.TestingConnection");

        try
        {
            await connectionTester.TestChatAsync(agent.Provider, agent.Endpoint, agent.APIKey, agent.ModelName);

            agent.ConnectionSuccess = true;
            agent.ConnectionMessage = Loc.Get("Settings.ConnectionSuccess", agent.ModelName);
        }
        catch (Exception ex)
        {
            agent.ConnectionSuccess = false;
            agent.ConnectionMessage = Loc.Get("Settings.ConnectionFailed", ex.Message);
        }
        finally
        {
            agent.IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private async Task TestEmbeddingConnectionAsync()
    {
        Embedding.IsTestingConnection = true;
        Embedding.ConnectionSuccess   = null;
        Embedding.ConnectionMessage   = Loc.Get("Settings.TestingConnection");

        try
        {
            await connectionTester.TestEmbeddingAsync(Embedding.Provider, Embedding.Endpoint, Embedding.APIKey, Embedding.ModelName);

            Embedding.ConnectionSuccess = true;
            Embedding.ConnectionMessage = Loc.Get("Settings.ConnectionSuccess", Embedding.ModelName);
        }
        catch (Exception ex)
        {
            Embedding.ConnectionSuccess = false;
            Embedding.ConnectionMessage = Loc.Get("Settings.ConnectionFailed", ex.Message);
        }
        finally
        {
            Embedding.IsTestingConnection = false;
        }
    }
}
