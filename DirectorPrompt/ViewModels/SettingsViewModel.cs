using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace DirectorPrompt.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IConfiguration configuration;
    private readonly IModelConnectionTester connectionTester;

    private readonly Dictionary<string, string[]> agentTools = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool isSaving;

    [ObservableProperty]
    private string validationMessage = string.Empty;

    [ObservableProperty]
    private string databasePath = string.Empty;

    [ObservableProperty]
    private int snapshotInterval;

    public ObservableCollection<AgentSettingViewModel> Agents { get; } = [];

    public SettingsViewModel(IConfiguration configuration, IModelConnectionTester connectionTester)
    {
        this.configuration     = configuration;
        this.connectionTester  = connectionTester;
        LoadSettings();
    }

    private void LoadSettings()
    {
        DatabasePath = configuration["Database:Path"] ?? "data/director.db";
        SnapshotInterval = configuration.GetValue<int>("Orchestrator:SnapshotInterval");

        LoadAgents();
    }

    private void LoadAgents()
    {
        var agents = configuration.GetSection("Orchestrator:Agents").Get<List<AgentDefinition>>();

        if (agents is null || agents.Count == 0)
        {
            return;
        }

        foreach (var agent in agents)
        {
            agentTools[agent.Name] = agent.Tools;

            Agents.Add(new AgentSettingViewModel
            {
                Name = agent.Name,
                Role = agent.Role,
                Provider = agent.ModelConfig.Provider,
                Endpoint = agent.ModelConfig.Endpoint,
                APIKey = agent.ModelConfig.APIKey ?? string.Empty,
                ModelName = agent.ModelConfig.ModelName,
                Temperature = agent.Temperature,
                Enabled = agent.Enabled,
                MaxRetries = agent.MaxRetries
            });
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;

        try
        {
            var json = BuildSettingsJSON();
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            await File.WriteAllTextAsync(path, json);

            ValidationMessage = "设置已保存, 部分配置需要重启应用后生效";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存设置失败");
            ValidationMessage = $"保存失败: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private string BuildSettingsJSON()
    {
        var root = new JsonObject
        {
            ["Database"] = new JsonObject
            {
                ["Path"] = DatabasePath
            },
            ["Orchestrator"] = BuildOrchestratorJSON(),
            ["Embedding"] = new JsonObject
            {
                ["Provider"] = configuration["Embedding:Provider"] ?? "openai",
                ["Endpoint"] = configuration["Embedding:Endpoint"] ?? string.Empty,
                ["ApiKey"] = configuration["Embedding:ApiKey"] ?? string.Empty,
                ["ModelName"] = configuration["Embedding:ModelName"] ?? "text-embedding-3-small"
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        return root.ToJsonString(options);
    }

    private JsonObject BuildOrchestratorJSON()
    {
        var agentsArray = new JsonArray();

        foreach (var agent in Agents)
        {
            var tools = agentTools.TryGetValue(agent.Name, out var t)
                ? t
                : [];

            var toolNodes = tools.Select(tool => (JsonNode)tool).ToList();

            var agentObj = new JsonObject
            {
                ["Name"] = agent.Name,
                ["Role"] = agent.Role.ToString(),
                ["ModelConfig"] = new JsonObject
                {
                    ["Provider"] = agent.Provider,
                    ["Endpoint"] = agent.Endpoint,
                    ["APIKey"] = agent.APIKey,
                    ["ModelName"] = agent.ModelName
                },
                ["SystemPrompt"] = string.Empty,
                ["Temperature"] = agent.Temperature,
                ["Tools"] = new JsonArray([.. toolNodes]),
                ["Enabled"] = agent.Enabled
            };

            if (agent.MaxRetries.HasValue)
            {
                agentObj["MaxRetries"] = agent.MaxRetries.Value;
            }

            agentsArray.Add(agentObj);
        }

        var auditSection = configuration.GetSection("Orchestrator:AuditConfig");
        var dimensionNodes = auditSection.GetSection("Dimensions")
            .Get<List<string>>()
            ?.Select(d => (JsonNode)d)
            .ToList() ?? [];

        var memorySection = configuration.GetSection("Orchestrator:MemoryConfig");
        var knowledgeSection = configuration.GetSection("Orchestrator:KnowledgeConfig");

        return new JsonObject
        {
            ["Agents"] = agentsArray,
            ["AuditConfig"] = new JsonObject
            {
                ["Mode"] = auditSection["Mode"] ?? "Blocking",
                ["MaxRetries"] = auditSection.GetValue<int?>("MaxRetries") ?? 2,
                ["Dimensions"] = new JsonArray([.. dimensionNodes])
            },
            ["MemoryConfig"] = new JsonObject
            {
                ["RecallTopK"] = memorySection.GetValue<int?>("RecallTopK") ?? 10,
                ["TokenBudget"] = memorySection.GetValue<int?>("TokenBudget") ?? 1500,
                ["MinRelevance"] = memorySection.GetValue<float?>("MinRelevance") ?? 0,
                ["TimeDecayLambda"] = memorySection.GetValue<float?>("TimeDecayLambda") ?? 0
            },
            ["KnowledgeConfig"] = new JsonObject
            {
                ["SemanticTopK"] = knowledgeSection.GetValue<int?>("SemanticTopK") ?? 8,
                ["TokenBudget"] = knowledgeSection.GetValue<int?>("TokenBudget") ?? 2000,
                ["MinRelevance"] = knowledgeSection.GetValue<float?>("MinRelevance") ?? 0
            },
            ["SnapshotInterval"] = SnapshotInterval
        };
    }

    [RelayCommand]
    private async Task TestAgentConnectionAsync(AgentSettingViewModel? agent)
    {
        if (agent is null)
        {
            return;
        }

        agent.IsTestingConnection = true;
        agent.ConnectionSuccess   = null;
        agent.ConnectionMessage   = "正在测试连接…";

        try
        {
            await connectionTester.TestChatAsync(agent.Provider, agent.Endpoint, agent.APIKey, agent.ModelName);

            agent.ConnectionSuccess = true;
            agent.ConnectionMessage = $"连接成功, 模型: {agent.ModelName}";
        }
        catch (Exception ex)
        {
            agent.ConnectionSuccess = false;
            agent.ConnectionMessage = $"连接失败: {ex.Message}";
        }
        finally
        {
            agent.IsTestingConnection = false;
        }
    }
}
