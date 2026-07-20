using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Agents.MCP;
using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Localization;
using Serilog;

namespace DirectorPrompt.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IModelConnectionTester   connectionTester;
    private readonly ILocalizationService     localizationService;
    private readonly UserSettings             userSettings;
    private readonly IUserSettingsStore       userSettingsStore;
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
        UserSettings             userSettings,
        IModelConnectionTester   connectionTester,
        ILocalizationService     localizationService,
        IUserSettingsStore       userSettingsStore,
        IExternalMCPToolRegistry externalMCPToolRegistry,
        IDirectorPromptMCPStatus directorPromptMCPStatus
    )
    {
        this.connectionTester        = connectionTester;
        this.localizationService     = localizationService;
        this.userSettings            = userSettings;
        this.userSettingsStore       = userSettingsStore;
        this.externalMCPToolRegistry = externalMCPToolRegistry;
        this.directorPromptMCPStatus = directorPromptMCPStatus;

        SelectedLanguage    = userSettings.Localization.Language;
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
            (task => new AgentTaskSettingViewModel(task, userSettings.MCPServers)
            )
        );
        Embedding = new EmbeddingSettingViewModel(userSettings.EmbeddingConfig);
        Memory    = new MemorySettingViewModel(userSettings.Orchestrator.MemoryConfig);
        Knowledge = new KnowledgeSettingViewModel(userSettings.Orchestrator.KnowledgeConfig);

        Log.Information
        (
            "设置视图模型已初始化: 提供商数={ProviderCount}, 模型数={ModelCount}, 提示词数={PromptCount}, MCP服务数={MCPServerCount}, 局域网共享={LanSharingEnabled}",
            Providers.Count,
            Models.Count,
            Prompts.Count,
            MCPServers.Count,
            IsLanSharingEnabled
        );

        _         = InitializeMCPServersAsync();
    }

    private async Task InitializeMCPServersAsync()
    {
        await Task.Delay(100);

        var enabledServerCount = MCPServers.Count(server => server.Enabled);

        Log.Debug("开始自动检查 MCP 服务: 已启用服务数={EnabledServerCount}", enabledServerCount);

        foreach (var server in MCPServers)
        {
            if (server.Enabled)
                _ = RefreshMCPServerAsync(server);
        }
    }

    private void EnsureAgentTasks()
    {
        var existing = userSettings.Orchestrator.AgentTasks.ToDictionary(t => t.TaskType);
        var added     = 0;

        foreach (var taskType in Enum.GetValues<AgentTaskType>())
        {
            if (!existing.ContainsKey(taskType))
            {
                userSettings.Orchestrator.AgentTasks.Add
                (
                    new AgentTaskConfig { TaskType = taskType }
                );
                added++;
            }
        }

        if (added > 0)
            Log.Information("已补齐缺失的 Agent 任务配置: 数量={TaskCount}", added);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (string.IsNullOrEmpty(value) || !AvailableLanguages.ContainsKey(value))
            return;

        if (localizationService.CurrentLanguage != value)
        {
            Log.Information("切换界面语言: 原语言={CurrentLanguage}, 新语言={NewLanguage}", localizationService.CurrentLanguage, value);
            localizationService.LoadLanguage(value);
        }
    }

    [RelayCommand]
    private void AddProvider()
    {
        var config = new ProviderConfig { DisplayName = "新提供商" };
        userSettings.Orchestrator.Providers.Add(config);
        Providers.Add(new ProviderSettingViewModel(config));
        Log.Information("新增模型提供商配置: 提供商配置={ProviderID}", config.ID);
    }

    [RelayCommand]
    private void RemoveProvider(ProviderSettingViewModel? provider)
    {
        if (provider is null)
            return;

        userSettings.Orchestrator.Providers.Remove(provider.Config);
        Providers.Remove(provider);
        Log.Information("删除模型提供商配置: 提供商配置={ProviderID}", provider.ID);
    }

    [RelayCommand]
    private void AddModel()
    {
        var config = new ModelConfig { DisplayName = "新模型" };
        userSettings.Orchestrator.Models.Add(config);
        Models.Add(new ModelSettingViewModel(config));
        Log.Information("新增模型配置: 模型配置={ModelID}", config.ID);
    }

    [RelayCommand]
    private void RemoveModel(ModelSettingViewModel? model)
    {
        if (model is null)
            return;

        userSettings.Orchestrator.Models.Remove(model.Config);
        Models.Remove(model);
        Log.Information("删除模型配置: 模型配置={ModelID}, 模型={ModelName}", model.ID, model.ModelName);
    }

    [RelayCommand]
    private void AddPrompt(object? parameter = null)
    {
        var presetType = parameter as string;

        var displayName = string.IsNullOrEmpty(presetType) ?
                              Loc.Get("Settings.Prompt.NewPromptDefaultName") :
                              presetType switch
                              {
                                  "Narrator"     => Loc.Get("Settings.Prompt.Preset.Narrator"),
                                  "MemoryUpdate" => Loc.Get("Settings.Prompt.Preset.MemoryUpdate"),
                                  "Scene"        => Loc.Get("Settings.Prompt.Preset.Scene"),
                                  "SceneSummary" => Loc.Get("Settings.Prompt.Preset.SceneSummary"),
                                  _              => Loc.Get("Settings.Prompt.NewPromptDefaultName")
                              };

        var content = string.IsNullOrEmpty(presetType) ?
                          string.Empty :
                          presetType switch
                          {
                              "Narrator"     => NarratorPrompt.SYSTEM,
                              "MemoryUpdate" => MemorySubAgentPrompt.UPDATE,
                              "Scene"        => SceneAgentPrompt.SYSTEM,
                              "SceneSummary" => SceneSummaryPrompt.SYSTEM,
                              _              => string.Empty
                          };

        var config = new PromptConfig
        {
            DisplayName = displayName,
            Content     = content
        };
        userSettings.Orchestrator.Prompts.Add(config);
        Prompts.Add(new PromptSettingViewModel(config));
        Log.Information("新增提示词配置: 提示词配置={PromptID}, 预设类型={PresetType}, 内容长度={ContentLength}", config.ID, presetType, content.Length);
    }

    [RelayCommand]
    private void RemovePrompt(PromptSettingViewModel? prompt)
    {
        if (prompt is null)
            return;

        userSettings.Orchestrator.Prompts.Remove(prompt.Config);
        Prompts.Remove(prompt);
        Log.Information("删除提示词配置: 提示词配置={PromptID}", prompt.ID);
    }

    [RelayCommand]
    private void AddMCPServer()
    {
        var config = new MCPServerConfig { DisplayName = "新 MCP 服务" };
        userSettings.MCPServers.Add(config);
        MCPServers.Add(new MCPServerSettingViewModel(config));

        foreach (var task in AgentTasks)
            task.AddMCPServer(config);

        Log.Information("新增 MCP 服务配置: 服务配置={MCPServerID}", config.ID);
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

        Log.Information("删除 MCP 服务配置: 服务配置={MCPServerID}", server.Config.ID);
    }

    [RelayCommand]
    private async Task TestMCPServerAsync(MCPServerSettingViewModel? server)
    {
        if (server is null)
            return;

        server.Apply();
        server.IsTesting         = true;
        server.InspectionMessage = "正在连接";

        Log.Information("开始测试 MCP 服务连接: 服务配置={MCPServerID}, 传输类型={TransportType}", server.Config.ID, server.Config.Transport);

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

            Log.Information
            (
                "MCP 服务连接测试完成: 服务配置={MCPServerID}, 可用={IsAvailable}, 工具数={ToolCount}, 错误={ErrorMessage}",
                server.Config.ID,
                inspection.IsAvailable,
                inspection.Tools.Count,
                inspection.ErrorMessage
            );
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
        server.IsTesting         = true;
        server.InspectionMessage = "正在刷新工具";

        Log.Information("开始刷新 MCP 服务工具: 服务配置={MCPServerID}, 传输类型={TransportType}", server.Config.ID, server.Config.Transport);

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

            Log.Information
            (
                "MCP 服务工具刷新完成: 服务配置={MCPServerID}, 可用={IsAvailable}, 工具数={ToolCount}, 错误={ErrorMessage}",
                server.Config.ID,
                inspection.IsAvailable,
                inspection.Tools.Count,
                inspection.ErrorMessage
            );
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

        Log.Information
        (
            "开始保存设置: 提供商数={ProviderCount}, 模型数={ModelCount}, 提示词数={PromptCount}, MCP服务数={MCPServerCount}, 局域网共享={LanSharingEnabled}",
            Providers.Count,
            Models.Count,
            Prompts.Count,
            MCPServers.Count,
            IsLanSharingEnabled
        );

        try
        {
            foreach (var server in MCPServers)
                server.Apply();

            userSettings.Localization.Language             = SelectedLanguage;
            userSettings.RemoteControl.IsLanSharingEnabled = IsLanSharingEnabled;

            await userSettingsStore.SaveAsync(userSettings);
            await externalMCPToolRegistry.InvalidateAsync();

            SaveSuccess = true;

            Log.Information("设置保存完成: 当前语言={Language}, 局域网共享={LanSharingEnabled}", SelectedLanguage, IsLanSharingEnabled);
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

        Log.Information("开始获取聊天模型列表: 提供商={Provider}, 模型配置={ModelID}", provider.Provider, model.ID);

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
            Log.Information("聊天模型列表获取完成: 提供商={Provider}, 模型配置={ModelID}, 模型数={ModelCount}", provider.Provider, model.ID, model.AvailableModels.Count);
        }
        catch (Exception ex)
        {
            model.ModelFetchMessage = Loc.Get("Settings.FetchModelsFailed", ex.Message);
            Log.Error(ex, "获取聊天模型列表失败: 提供商={Provider}, 模型配置={ModelID}", provider.Provider, model.ID);
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

        Log.Information("开始测试聊天模型连接: 提供商={Provider}, 模型={Model}", provider.Provider, model.ModelName);

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
            Log.Information("聊天模型连接测试成功: 提供商={Provider}, 模型={Model}", provider.Provider, model.ModelName);
        }
        catch (Exception ex)
        {
            model.ConnectionSuccess = false;
            model.ConnectionMessage = Loc.Get("Settings.ConnectionFailed", ex.Message);
            Log.Error(ex, "聊天模型连接测试失败: 提供商={Provider}, 模型={Model}", provider.Provider, model.ModelName);
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

        Log.Information("开始获取向量模型列表: 提供商={Provider}", provider.Provider);

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
            Log.Information("向量模型列表获取完成: 提供商={Provider}, 模型数={ModelCount}", provider.Provider, Embedding.AvailableModels.Count);
        }
        catch (Exception ex)
        {
            Embedding.ModelFetchMessage = Loc.Get("Settings.FetchModelsFailed", ex.Message);
            Log.Error(ex, "获取向量模型列表失败: 提供商={Provider}", provider.Provider);
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

        Log.Information("开始测试向量模型连接: 提供商={Provider}, 模型={Model}", provider.Provider, Embedding.ModelName);

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
            Log.Information("向量模型连接测试成功: 提供商={Provider}, 模型={Model}", provider.Provider, Embedding.ModelName);
        }
        catch (Exception ex)
        {
            Embedding.ConnectionSuccess = false;
            Embedding.ConnectionMessage = Loc.Get("Settings.ConnectionFailed", ex.Message);
            Log.Error(ex, "向量模型连接测试失败: 提供商={Provider}, 模型={Model}", provider.Provider, Embedding.ModelName);
        }
        finally
        {
            Embedding.IsTestingConnection = false;
        }
    }
}
