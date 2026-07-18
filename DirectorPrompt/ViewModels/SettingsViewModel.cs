using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Agents.MCP;
using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Localization;
using DirectorPrompt.MCP;
using Serilog;

namespace DirectorPrompt.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IModelConnectionTester connectionTester;
    private readonly ILocalizationService   localizationService;
    private readonly UserSettings           userSettings;
    private readonly IUserSettingsStore     userSettingsStore;
    private readonly IExternalMCPToolRegistry externalMCPToolRegistry;
    private readonly IDirectorPromptMCPStatus directorPromptMCPStatus;

    public bool SaveSuccess { get; private set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string ValidationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLanSharingEnabled { get; set; }

    public ObservableCollection<ProviderSettingViewModel> Providers { get; }

    public ObservableCollection<ModelSettingViewModel> Models { get; }

    public ObservableCollection<PromptSettingViewModel> Prompts { get; }

    public ObservableCollection<AgentTaskSettingViewModel> AgentTasks { get; }

    public ObservableCollection<MCPServerSettingViewModel> MCPServers { get; }

    public EmbeddingSettingViewModel Embedding { get; }

    public MemorySettingViewModel Memory { get; }

    public KnowledgeSettingViewModel Knowledge { get; }

    public IReadOnlyList<MCPTransportType> AvailableMCPTransports { get; } = Enum.GetValues<MCPTransportType>();

    public string InternalMCPEndpoint => directorPromptMCPStatus.Endpoint;

    public string InternalMCPStatus => directorPromptMCPStatus.IsAvailable ?
                                           "运行中" :
                                           $"不可用: {directorPromptMCPStatus.ErrorMessage ?? "未启动"}";

    public bool IsInternalMCPAvailable => directorPromptMCPStatus.IsAvailable;

    public IReadOnlyDictionary<string, string> AvailableLanguages =>
        localizationService.AvailableLanguages;

    public SettingsViewModel
    (
        UserSettings           userSettings,
        IModelConnectionTester connectionTester,
        ILocalizationService   localizationService,
        IUserSettingsStore     userSettingsStore,
        IExternalMCPToolRegistry externalMCPToolRegistry,
        IDirectorPromptMCPStatus directorPromptMCPStatus
    )
    {
        this.connectionTester    = connectionTester;
        this.localizationService = localizationService;
        this.userSettings        = userSettings;
        this.userSettingsStore   = userSettingsStore;
        this.externalMCPToolRegistry = externalMCPToolRegistry;
        this.directorPromptMCPStatus = directorPromptMCPStatus;

        SelectedLanguage = userSettings.Localization.Language;
        IsLanSharingEnabled = userSettings.RemoteControl.IsLanSharingEnabled;

        if (string.IsNullOrEmpty(SelectedLanguage))
            SelectedLanguage = localizationService.CurrentLanguage;

        EnsureAgentTasks();

        Providers = new ObservableCollection<ProviderSettingViewModel>
        (
            userSettings.Orchestrator.Providers.Select(p => new ProviderSettingViewModel(p))
        );
        Models = new ObservableCollection<ModelSettingViewModel>
        (
            userSettings.Orchestrator.Models.Select(m => new ModelSettingViewModel(m))
        );
        Prompts = new ObservableCollection<PromptSettingViewModel>
        (
            userSettings.Orchestrator.Prompts.Select(p => new PromptSettingViewModel(p))
        );
        MCPServers = new ObservableCollection<MCPServerSettingViewModel>
        (
            userSettings.MCPServers.Select(server => new MCPServerSettingViewModel(server))
        );
        AgentTasks = new ObservableCollection<AgentTaskSettingViewModel>
        (
            userSettings.Orchestrator.AgentTasks.Select
            (
                task => new AgentTaskSettingViewModel(task, userSettings.MCPServers)
            )
        );
        Embedding = new EmbeddingSettingViewModel(userSettings.EmbeddingConfig);
        Memory    = new MemorySettingViewModel(userSettings.Orchestrator.MemoryConfig);
        Knowledge = new KnowledgeSettingViewModel(userSettings.Orchestrator.KnowledgeConfig);
        _ = InitializeMCPServersAsync();
    }

    private async Task InitializeMCPServersAsync()
    {
        await Task.Delay(100);

        foreach (var server in MCPServers)
        {
            if (server.Enabled)
            {
                _ = RefreshMCPServerAsync(server);
            }
        }
    }

    private void EnsureAgentTasks()
    {
        var existing = userSettings.Orchestrator.AgentTasks.ToDictionary(t => t.TaskType);

        foreach (var taskType in Enum.GetValues<AgentTaskType>())
        {
            if (!existing.ContainsKey(taskType))
            {
                userSettings.Orchestrator.AgentTasks.Add
                (
                    new AgentTaskConfig { TaskType = taskType }
                );
            }
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
    private void AddProvider()
    {
        var config = new ProviderConfig { DisplayName = "新提供商" };
        userSettings.Orchestrator.Providers.Add(config);
        Providers.Add(new ProviderSettingViewModel(config));
    }

    [RelayCommand]
    private void RemoveProvider(ProviderSettingViewModel? provider)
    {
        if (provider is null)
            return;

        userSettings.Orchestrator.Providers.Remove(provider.Config);
        Providers.Remove(provider);
    }

    [RelayCommand]
    private void AddModel()
    {
        var config = new ModelConfig { DisplayName = "新模型" };
        userSettings.Orchestrator.Models.Add(config);
        Models.Add(new ModelSettingViewModel(config));
    }

    [RelayCommand]
    private void RemoveModel(ModelSettingViewModel? model)
    {
        if (model is null)
            return;

        userSettings.Orchestrator.Models.Remove(model.Config);
        Models.Remove(model);
    }

    [RelayCommand]
    private void AddPrompt(object? parameter = null)
    {
        string? presetType = parameter as string;

        var displayName = string.IsNullOrEmpty(presetType) ?
                              Loc.Get("Settings.Prompt.NewPromptDefaultName") :
                              presetType switch
                              {
                                  "Narrator" => Loc.Get("Settings.Prompt.Preset.Narrator"),
                                  "MemoryUpdate" => Loc.Get("Settings.Prompt.Preset.MemoryUpdate"),
                                  "Scene" => Loc.Get("Settings.Prompt.Preset.Scene"),
                                  "SceneSummary" => Loc.Get("Settings.Prompt.Preset.SceneSummary"),
                                  _ => Loc.Get("Settings.Prompt.NewPromptDefaultName")
                              };

        var content = string.IsNullOrEmpty(presetType) ?
                          string.Empty :
                          presetType switch
                          {
                              "Narrator" => NarratorPrompt.SYSTEM,
                              "MemoryUpdate" => MemorySubAgentPrompt.UPDATE,
                              "Scene" => SceneAgentPrompt.SYSTEM,
                              "SceneSummary" => SceneSummaryPrompt.SYSTEM,
                              _ => string.Empty
                          };

        var config = new PromptConfig
        {
            DisplayName = displayName,
            Content     = content
        };
        userSettings.Orchestrator.Prompts.Add(config);
        Prompts.Add(new PromptSettingViewModel(config));
    }

    [RelayCommand]
    private void RemovePrompt(PromptSettingViewModel? prompt)
    {
        if (prompt is null)
            return;

        userSettings.Orchestrator.Prompts.Remove(prompt.Config);
        Prompts.Remove(prompt);
    }

    [RelayCommand]
    private void AddMCPServer()
    {
        var config = new MCPServerConfig { DisplayName = "新 MCP 服务" };
        userSettings.MCPServers.Add(config);
        MCPServers.Add(new MCPServerSettingViewModel(config));

        foreach (var task in AgentTasks)
            task.AddMCPServer(config);
    }

    [RelayCommand]
    private void RemoveMCPServer(MCPServerSettingViewModel? server)
    {
        if (server is null)
            return;

        userSettings.MCPServers.Remove(server.Config);
        MCPServers.Remove(server);

        foreach (var task in AgentTasks)
            task.RemoveMCPServer(server.Config.ID);
    }

    [RelayCommand]
    private async Task TestMCPServerAsync(MCPServerSettingViewModel? server)
    {
        if (server is null)
            return;

        server.Apply();
        server.IsTesting = true;
        server.InspectionMessage = "正在连接";

        try
        {
            var inspection = await externalMCPToolRegistry.InspectAsync(server.Config, false);
            server.ConnectionStatus = inspection.IsAvailable;
            server.ToolNames.Clear();
            if (inspection.IsAvailable)
            {
                foreach (var tool in inspection.Tools)
                    server.ToolNames.Add(tool);
            }
            server.InspectionMessage = inspection.IsAvailable ?
                                           $"连接成功, 发现 {inspection.Tools.Count} 个工具: {string.Join(", ", inspection.Tools.Select(t => t.Name))}" :
                                           $"连接失败: {inspection.ErrorMessage}";
        }
        finally
        {
            server.IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task RefreshMCPServerAsync(MCPServerSettingViewModel? server)
    {
        if (server is null)
            return;

        server.Apply();
        server.IsTesting = true;
        server.InspectionMessage = "正在刷新工具";

        try
        {
            var inspection = await externalMCPToolRegistry.InspectAsync(server.Config, true);
            server.ConnectionStatus = inspection.IsAvailable;
            server.ToolNames.Clear();
            if (inspection.IsAvailable)
            {
                foreach (var tool in inspection.Tools)
                    server.ToolNames.Add(tool);
            }
            server.InspectionMessage = inspection.IsAvailable ?
                                           $"已刷新, 发现 {inspection.Tools.Count} 个工具: {string.Join(", ", inspection.Tools.Select(t => t.Name))}" :
                                           $"刷新失败: {inspection.ErrorMessage}";
        }
        finally
        {
            server.IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;

        try
        {
            foreach (var server in MCPServers)
                server.Apply();

            userSettings.Localization.Language = SelectedLanguage;
            userSettings.RemoteControl.IsLanSharingEnabled = IsLanSharingEnabled;

            await userSettingsStore.SaveAsync(userSettings);
            await externalMCPToolRegistry.InvalidateAsync();

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
            var models = await connectionTester.FetchModelsAsync
                         (
                             provider.Provider,
                             provider.Endpoint,
                             provider.APIKey,
                             provider.CustomHeaders
                         );

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
            await connectionTester.TestChatAsync
            (
                provider.Provider,
                provider.Endpoint,
                provider.APIKey,
                model.ModelName,
                provider.CustomHeaders
            );

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
            var models = await connectionTester.FetchModelsAsync
                         (
                             provider.Provider,
                             provider.Endpoint,
                             provider.APIKey,
                             provider.CustomHeaders
                         );

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
            await connectionTester.TestEmbeddingAsync
            (
                provider.Provider,
                provider.Endpoint,
                provider.APIKey,
                Embedding.ModelName,
                provider.CustomHeaders
            );

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
