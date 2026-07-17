using System.Text.Json;
using System.Text.Json.Nodes;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Infrastructure;

public sealed class UserSettingsStore : IUserSettingsStore
{
    private readonly string userSettingsPath;

    public UserSettingsStore(string? userSettingsPath = null) =>
        this.userSettingsPath = userSettingsPath ?? AppPaths.UserSettingsPath;

    public UserSettings Load()
    {
        MigrateIfNeeded();

        if (!File.Exists(userSettingsPath))
            return new UserSettings();

        var json = File.ReadAllText(userSettingsPath);

        return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions.Default) ?? new UserSettings();
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions.Default);

        var directory = Path.GetDirectoryName(userSettingsPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(userSettingsPath, json, cancellationToken);
    }

    public bool MigrateIfNeeded()
    {
        if (!File.Exists(userSettingsPath))
            return false;

        var json = File.ReadAllText(userSettingsPath);
        var doc  = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("Orchestrator", out var orch) &&
            !doc.RootElement.TryGetProperty("orchestrator", out orch))
            return false;

        if (!orch.TryGetProperty("Providers", out _) &&
            orch.TryGetProperty("Agents", out var agentsEl) &&
            agentsEl.ValueKind == JsonValueKind.Array)
        {
            var settings = MigrateFromLegacy(doc.RootElement);
            File.WriteAllText(userSettingsPath, JsonSerializer.Serialize(settings, JsonOptions.Default));
            return true;
        }

        return RemoveObsoleteAgentTasks(userSettingsPath, json);
    }

    private static UserSettings MigrateFromLegacy(JsonElement root)
    {
        var providers  = new List<ProviderConfig>();
        var models     = new List<ModelConfig>();
        var prompts    = new List<PromptConfig>();
        var agentTasks = new List<AgentTaskConfig>();

        var orchEl = root.GetProperty("Orchestrator");
        var agents = orchEl.GetProperty("Agents").EnumerateArray().ToList();

        var providerCache = new Dictionary<string, ProviderConfig>();

        foreach (var agent in agents)
        {
            var roleStr  = agent.GetProperty("Role").GetString() ?? "Narrator";
            var modelCfg = agent.GetProperty("ModelConfig");
            var systemPpt = agent.TryGetProperty("SystemPrompt", out var sp) ?
                                sp.GetString() :
                                null;
            var temperature = agent.TryGetProperty("Temperature", out var t) ?
                                  t.GetSingle() :
                                  0.8f;

            var providerVal = modelCfg.GetProperty("Provider").GetString() ?? "openai";
            var endpoint    = modelCfg.GetProperty("Endpoint").GetString() ?? string.Empty;
            var apiKey = modelCfg.TryGetProperty("APIKey", out var ak) ?
                             ak.GetString() :
                             null;
            var modelName = modelCfg.GetProperty("ModelName").GetString() ?? string.Empty;

            var providerKey = $"{providerVal}|{endpoint}|{apiKey}";

            if (!providerCache.TryGetValue(providerKey, out var provider))
            {
                provider = new ProviderConfig
                {
                    DisplayName = providerVal.Equals("openai", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(endpoint) ?
                                      "OpenAI" :
                                      providerVal,
                    Provider = providerVal,
                    Endpoint = endpoint,
                    APIKey   = apiKey
                };

                providerCache[providerKey] = provider;
                providers.Add(provider);
            }

            string? promptID = null;

            if (!string.IsNullOrWhiteSpace(systemPpt))
            {
                var prompt = new PromptConfig
                {
                    DisplayName = $"{roleStr} 提示词",
                    Content     = systemPpt
                };

                prompts.Add(prompt);
                promptID = prompt.ID;
            }

            var model = new ModelConfig
            {
                DisplayName     = $"{roleStr} - {modelName}",
                ProviderID      = provider.ID,
                ModelName       = modelName,
                Temperature     = temperature,
                ReasoningEffort = null,
                PromptID        = promptID
            };

            models.Add(model);

            var taskType = roleStr switch
            {
                "Narrator" => AgentTaskType.Narrator,
                "Memory" or "State" => AgentTaskType.MemoryUpdate,
                "Scene" => AgentTaskType.Scene,
                _ => (AgentTaskType?)null
            };

            if (taskType is { } activeTaskType)
            {
                agentTasks.Add
                (
                    new AgentTaskConfig
                    {
                        TaskType      = activeTaskType,
                        ModelConfigID = model.ID,
                        Enabled       = true
                    }
                );
            }
        }

        var embeddingConfig = new EmbeddingConfig();

        if (root.TryGetProperty("EmbeddingConfig", out var embEl))
        {
            var embProvider = embEl.TryGetProperty("Provider", out var ep) ?
                                  ep.GetString() ?? "openai" :
                                  "openai";
            var embEndpoint = embEl.TryGetProperty("Endpoint", out var ee) ?
                                  ee.GetString() ?? string.Empty :
                                  string.Empty;
            var embAPIKey = embEl.TryGetProperty("APIKey", out var ek) ?
                                ek.GetString() :
                                null;
            var embModel = embEl.TryGetProperty("ModelName", out var em) ?
                               em.GetString() ?? "text-embedding-v4" :
                               "text-embedding-v4";

            var embProviderKey = $"{embProvider}|{embEndpoint}|{embAPIKey}";

            if (!providerCache.TryGetValue(embProviderKey, out var embProviderCfg))
            {
                embProviderCfg = new ProviderConfig
                {
                    DisplayName = "Embedding Provider",
                    Provider    = embProvider,
                    Endpoint    = embEndpoint,
                    APIKey      = embAPIKey
                };

                providerCache[embProviderKey] = embProviderCfg;
                providers.Add(embProviderCfg);
            }

            embeddingConfig = new EmbeddingConfig
            {
                ProviderID = embProviderCfg.ID,
                ModelName  = embModel
            };
        }

        return new UserSettings
        {
            Orchestrator = new OrchestratorConfig
            {
                Providers  = providers,
                Models     = models,
                Prompts    = prompts,
                AgentTasks = agentTasks
            },
            EmbeddingConfig = embeddingConfig,
            Localization = root.TryGetProperty("Localization", out var locEl) ?
                               JsonSerializer.Deserialize<LocalizationConfig>(locEl.GetRawText(), JsonOptions.Default) ?? new() :
                               new(),
            Session = root.TryGetProperty("Session", out var sessEl) ?
                          JsonSerializer.Deserialize<SessionStateConfig>(sessEl.GetRawText(), JsonOptions.Default) ?? new() :
                          new()
        };
    }

    private static bool RemoveObsoleteAgentTasks(string userSettingsPath, string json)
    {
        var root = JsonNode.Parse(json)?.AsObject();

        if (root is null)
            return false;

        var orchestrator = root["Orchestrator"]?.AsObject() ?? root["orchestrator"]?.AsObject();
        var tasks = orchestrator?["AgentTasks"]?.AsArray() ?? orchestrator?["agentTasks"]?.AsArray();

        if (tasks is null)
            return false;

        var removed = false;

        for (var index = tasks.Count - 1; index >= 0; index--)
        {
            var task = tasks[index]?.AsObject();
            var taskType = task?["TaskType"]?.GetValue<string>() ?? task?["taskType"]?.GetValue<string>();

            if (taskType is not ("Knowledge" or "MemoryRecall"))
                continue;

            tasks.RemoveAt(index);
            removed = true;
        }

        if (removed)
            File.WriteAllText(userSettingsPath, root.ToJsonString(JsonOptions.Default));

        return removed;
    }
}
