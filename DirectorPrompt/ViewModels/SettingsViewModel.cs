using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
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
    private readonly OrchestratorConfig     orchestratorConfig;

    public bool SaveSuccess { get; private set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string ValidationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; } = string.Empty;

    public ObservableCollection<ProviderSettingViewModel> Providers { get; } = [];

    public ObservableCollection<ModelSettingViewModel> Models { get; } = [];

    public ObservableCollection<PromptSettingViewModel> Prompts { get; } = [];

    public ObservableCollection<AgentTaskSettingViewModel> AgentTasks { get; } = [];

    public EmbeddingSettingViewModel Embedding { get; } = new();

    public MemorySettingViewModel Memory { get; } = new();

    public KnowledgeSettingViewModel Knowledge { get; } = new();

    public IReadOnlyDictionary<string, string> AvailableLanguages =>
        localizationService.AvailableLanguages;

    public SettingsViewModel
    (
        UserSettings           userSettings,
        IModelConnectionTester connectionTester,
        ILocalizationService   localizationService,
        OrchestratorConfig     orchestratorConfig
    )
    {
        this.connectionTester    = connectionTester;
        this.localizationService = localizationService;
        this.userSettings        = userSettings;
        this.orchestratorConfig  = orchestratorConfig;

        LoadSettings(userSettings);
    }

    private void LoadSettings(UserSettings settings)
    {
        SelectedLanguage = settings.Localization.Language;

        if (string.IsNullOrEmpty(SelectedLanguage))
            SelectedLanguage = localizationService.CurrentLanguage;

        LoadProviders(settings.Orchestrator.Providers);
        LoadModels(settings.Orchestrator.Models);
        LoadPrompts(settings.Orchestrator.Prompts);
        LoadAgentTasks(settings.Orchestrator.AgentTasks);
        LoadEmbeddingConfig(settings.EmbeddingConfig);
        LoadMemoryConfig(settings.Orchestrator.MemoryConfig);
        LoadKnowledgeConfig(settings.Orchestrator.KnowledgeConfig);
    }

    private void LoadProviders(List<ProviderConfig> configs)
    {
        Providers.Clear();

        foreach (var config in configs)
        {
            Providers.Add
            (
                new ProviderSettingViewModel
                {
                    ID            = config.ID,
                    DisplayName   = config.DisplayName,
                    Provider      = config.Provider,
                    Endpoint      = config.Endpoint,
                    APIKey        = config.APIKey        ?? string.Empty,
                    CustomHeaders = config.CustomHeaders ?? string.Empty
                }
            );
        }
    }

    private void LoadModels(List<ModelConfig> configs)
    {
        Models.Clear();

        foreach (var config in configs)
        {
            Models.Add
            (
                new ModelSettingViewModel
                {
                    ID              = config.ID,
                    DisplayName     = config.DisplayName,
                    ProviderID      = config.ProviderID,
                    ModelName       = config.ModelName,
                    Temperature     = config.Temperature,
                    ReasoningEffort = config.ReasoningEffort ?? string.Empty,
                    ExtraParameters = config.ExtraParameters ?? string.Empty,
                    PromptID        = config.PromptID
                }
            );
        }
    }

    private void LoadPrompts(List<PromptConfig> configs)
    {
        Prompts.Clear();

        foreach (var config in configs)
        {
            Prompts.Add
            (
                new PromptSettingViewModel
                {
                    ID          = config.ID,
                    DisplayName = config.DisplayName,
                    Content     = config.Content
                }
            );
        }
    }

    private void LoadAgentTasks(List<AgentTaskConfig> configs)
    {
        AgentTasks.Clear();

        var existing = configs.ToDictionary(c => c.TaskType);

        foreach (var taskType in Enum.GetValues<AgentTaskType>())
        {
            if (existing.TryGetValue(taskType, out var config))
            {
                AgentTasks.Add
                (
                    new AgentTaskSettingViewModel
                    {
                        TaskType      = taskType,
                        ModelConfigID = config.ModelConfigID,
                        PromptID      = config.PromptID
                    }
                );
            }
            else
            {
                AgentTasks.Add
                (
                    new AgentTaskSettingViewModel
                    {
                        TaskType = taskType
                    }
                );
            }
        }
    }

    private void LoadEmbeddingConfig(EmbeddingConfig config)
    {
        Embedding.ProviderID = config.ProviderID;
        Embedding.ModelName  = config.ModelName;
    }

    private void LoadMemoryConfig(MemoryConfig config)
    {
        Memory.RecallTopK      = config.RecallTopK;
        Memory.TokenBudget     = config.TokenBudget;
        Memory.MinRelevance    = config.MinRelevance;
        Memory.TimeDecayLambda = config.TimeDecayLambda;
    }

    private void LoadKnowledgeConfig(KnowledgeRetrievalConfig config)
    {
        Knowledge.SemanticTopK = config.SemanticTopK;
        Knowledge.TokenBudget  = config.TokenBudget;
        Knowledge.MinRelevance = config.MinRelevance;
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (string.IsNullOrEmpty(value) || !AvailableLanguages.ContainsKey(value))
            return;

        if (localizationService.CurrentLanguage != value)
            localizationService.LoadLanguage(value);
    }

    [RelayCommand]
    private void AddProvider() =>
        Providers.Add(new ProviderSettingViewModel { DisplayName = "新提供商" });

    [RelayCommand]
    private void RemoveProvider(ProviderSettingViewModel? provider)
    {
        if (provider is not null)
            Providers.Remove(provider);
    }

    [RelayCommand]
    private void AddModel() =>
        Models.Add(new ModelSettingViewModel { DisplayName = "新模型" });

    [RelayCommand]
    private void RemoveModel(ModelSettingViewModel? model)
    {
        if (model is not null)
            Models.Remove(model);
    }

    [RelayCommand]
    private void AddPrompt() =>
        Prompts.Add(new PromptSettingViewModel { DisplayName = "新提示词" });

    [RelayCommand]
    private void RemovePrompt(PromptSettingViewModel? prompt)
    {
        if (prompt is not null)
            Prompts.Remove(prompt);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;

        try
        {
            var providers = Providers.Select
            (p => new ProviderConfig
                {
                    ID          = p.ID,
                    DisplayName = p.DisplayName,
                    Provider    = p.Provider,
                    Endpoint    = p.Endpoint,
                    APIKey      = p.APIKey,
                    CustomHeaders = string.IsNullOrWhiteSpace(p.CustomHeaders) ?
                                        null :
                                        p.CustomHeaders
                }
            ).ToList();

            var models = Models.Select
            (m => new ModelConfig
                {
                    ID          = m.ID,
                    DisplayName = m.DisplayName,
                    ProviderID  = m.ProviderID,
                    ModelName   = m.ModelName,
                    Temperature = m.Temperature,
                    ReasoningEffort = string.IsNullOrWhiteSpace(m.ReasoningEffort) ?
                                          null :
                                          m.ReasoningEffort,
                    ExtraParameters = string.IsNullOrWhiteSpace(m.ExtraParameters) ?
                                          null :
                                          m.ExtraParameters,
                    PromptID = m.PromptID
                }
            ).ToList();

            var prompts = Prompts.Select
            (p => new PromptConfig
                {
                    ID          = p.ID,
                    DisplayName = p.DisplayName,
                    Content     = p.Content
                }
            ).ToList();

            var tasks = AgentTasks.Select
            (t => new AgentTaskConfig
                {
                    TaskType      = t.TaskType,
                    ModelConfigID = t.ModelConfigID,
                    PromptID      = t.PromptID,
                    Enabled       = true
                }
            ).ToList();

            userSettings.Orchestrator.Providers  = providers;
            userSettings.Orchestrator.Models     = models;
            userSettings.Orchestrator.Prompts    = prompts;
            userSettings.Orchestrator.AgentTasks = tasks;

            var memoryConfig = new MemoryConfig
            {
                RecallTopK      = Memory.RecallTopK,
                TokenBudget     = Memory.TokenBudget,
                MinRelevance    = Memory.MinRelevance,
                TimeDecayLambda = Memory.TimeDecayLambda
            };

            var knowledgeConfig = new KnowledgeRetrievalConfig
            {
                SemanticTopK = Knowledge.SemanticTopK,
                TokenBudget  = Knowledge.TokenBudget,
                MinRelevance = Knowledge.MinRelevance
            };

            userSettings.Orchestrator.MemoryConfig   = memoryConfig;
            userSettings.Orchestrator.KnowledgeConfig = knowledgeConfig;

            orchestratorConfig.Providers  = providers;
            orchestratorConfig.Models     = models;
            orchestratorConfig.Prompts    = prompts;
            orchestratorConfig.AgentTasks = tasks;
            orchestratorConfig.MemoryConfig    = memoryConfig;
            orchestratorConfig.KnowledgeConfig = knowledgeConfig;

            userSettings.EmbeddingConfig = new EmbeddingConfig
            {
                ProviderID = Embedding.ProviderID,
                ModelName  = Embedding.ModelName
            };

            userSettings.Localization.Language = SelectedLanguage;

            await userSettings.SaveAsync();

            SaveSuccess = true;
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
    private async Task FetchModelsAsync(ModelSettingViewModel? model)
    {
        if (model is null)
            return;

        var provider = Providers.FirstOrDefault(p => p.ID == model.ProviderID);

        if (provider is null)
            return;

        model.IsFetchingModels  = true;
        model.ModelFetchMessage = Loc.Get("Settings.FetchingModels");

        try
        {
            var models = await connectionTester.FetchModelsAsync(provider.Provider, provider.Endpoint, provider.APIKey, provider.CustomHeaders);

            model.AvailableModels.Clear();
            foreach (var m in models)
                model.AvailableModels.Add(m);

            if (string.IsNullOrWhiteSpace(model.ModelName) && model.AvailableModels.Count > 0)
                model.ModelName = model.AvailableModels[0];

            model.ModelFetchMessage = Loc.Get("Settings.FetchModelsSuccess", model.AvailableModels.Count);
        }
        catch (Exception ex)
        {
            model.ModelFetchMessage = Loc.Get("Settings.FetchModelsFailed", ex.Message);
        }
        finally
        {
            model.IsFetchingModels = false;
        }
    }

    [RelayCommand]
    private async Task TestModelConnectionAsync(ModelSettingViewModel? model)
    {
        if (model is null)
            return;

        var provider = Providers.FirstOrDefault(p => p.ID == model.ProviderID);

        if (provider is null)
            return;

        model.IsTestingConnection = true;
        model.ConnectionSuccess   = null;
        model.ConnectionMessage   = Loc.Get("Settings.TestingConnection");

        try
        {
            await connectionTester.TestChatAsync(provider.Provider, provider.Endpoint, provider.APIKey, model.ModelName, provider.CustomHeaders);

            model.ConnectionSuccess = true;
            model.ConnectionMessage = Loc.Get("Settings.ConnectionSuccess", model.ModelName);
        }
        catch (Exception ex)
        {
            model.ConnectionSuccess = false;
            model.ConnectionMessage = Loc.Get("Settings.ConnectionFailed", ex.Message);
        }
        finally
        {
            model.IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private void ClearModelPrompt(ModelSettingViewModel? model)
    {
        if (model is not null)
            model.PromptID = null;
    }

    [RelayCommand]
    private void ClearTaskPrompt(AgentTaskSettingViewModel? task)
    {
        if (task is not null)
            task.PromptID = null;
    }

    [RelayCommand]
    private async Task FetchEmbeddingModelsAsync()
    {
        var provider = Providers.FirstOrDefault(p => p.ID == Embedding.ProviderID);

        if (provider is null)
            return;

        Embedding.IsFetchingModels  = true;
        Embedding.ModelFetchMessage = Loc.Get("Settings.FetchingModels");

        try
        {
            var models = await connectionTester.FetchModelsAsync(provider.Provider, provider.Endpoint, provider.APIKey, provider.CustomHeaders);

            Embedding.AvailableModels.Clear();
            foreach (var m in models)
                Embedding.AvailableModels.Add(m);

            if (string.IsNullOrWhiteSpace(Embedding.ModelName) && Embedding.AvailableModels.Count > 0)
                Embedding.ModelName = Embedding.AvailableModels[0];

            Embedding.ModelFetchMessage = Loc.Get("Settings.FetchModelsSuccess", Embedding.AvailableModels.Count);
        }
        catch (Exception ex)
        {
            Embedding.ModelFetchMessage = Loc.Get("Settings.FetchModelsFailed", ex.Message);
        }
        finally
        {
            Embedding.IsFetchingModels = false;
        }
    }

    [RelayCommand]
    private async Task TestEmbeddingConnectionAsync()
    {
        var provider = Providers.FirstOrDefault(p => p.ID == Embedding.ProviderID);

        if (provider is null)
            return;

        Embedding.IsTestingConnection = true;
        Embedding.ConnectionSuccess   = null;
        Embedding.ConnectionMessage   = Loc.Get("Settings.TestingConnection");

        try
        {
            await connectionTester.TestEmbeddingAsync(provider.Provider, provider.Endpoint, provider.APIKey, Embedding.ModelName, provider.CustomHeaders);

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
