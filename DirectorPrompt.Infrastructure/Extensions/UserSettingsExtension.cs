using System.Text.Json;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Infrastructure.Extensions;

public static class UserSettingsExtension
{
    extension(UserSettings settings)
    {
        public async Task SaveAsync()
        {
            var json = JsonSerializer.Serialize(settings, UserSettings.JSONOptions);

            Directory.CreateDirectory(AppPaths.DataDirectory);
            await File.WriteAllTextAsync(AppPaths.UserSettingsPath, json);
        }
    }

    public static bool MigrateIfNeeded()
    {
        if (!File.Exists(AppPaths.UserSettingsPath))
            return false;

        var json = File.ReadAllText(AppPaths.UserSettingsPath);
        var doc  = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("Orchestrator", out var orch))
            return false;

        if (orch.TryGetProperty("Providers", out _))
            return false;

        if (!orch.TryGetProperty("Agents", out var agentsEl) || agentsEl.ValueKind != JsonValueKind.Array)
            return false;

        var settings = MigrateFromLegacy(doc.RootElement);

        File.WriteAllText(AppPaths.UserSettingsPath, JsonSerializer.Serialize(settings, UserSettings.JSONOptions));

        return true;
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
                "Narrator"  => AgentTaskType.Narrator,
                "Knowledge" => AgentTaskType.Knowledge,
                "Memory"    => AgentTaskType.MemoryRecall,
                "Audit"     => AgentTaskType.Audit,
                "Scene"     => AgentTaskType.Scene,
                "State"     => AgentTaskType.MemoryUpdate,
                _           => AgentTaskType.Narrator
            };

            if (taskType == AgentTaskType.MemoryRecall)
            {
                agentTasks.Add
                (
                    new AgentTaskConfig
                    {
                        TaskType      = AgentTaskType.MemoryRecall,
                        ModelConfigID = model.ID,
                        Enabled       = true
                    }
                );

                agentTasks.Add
                (
                    new AgentTaskConfig
                    {
                        TaskType      = AgentTaskType.MemoryUpdate,
                        ModelConfigID = model.ID,
                        Enabled       = true
                    }
                );
            }
            else
            {
                agentTasks.Add
                (
                    new AgentTaskConfig
                    {
                        TaskType      = taskType,
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
            Orchestrator = new UserOrchestratorConfig
            {
                Providers  = providers,
                Models     = models,
                Prompts    = prompts,
                AgentTasks = agentTasks
            },
            EmbeddingConfig = embeddingConfig,
            Localization = root.TryGetProperty("Localization", out var locEl) ?
                               JsonSerializer.Deserialize<LocalizationConfig>(locEl.GetRawText(), UserSettings.JSONOptions) ?? new() :
                               new(),
            Session = root.TryGetProperty("Session", out var sessEl) ?
                          JsonSerializer.Deserialize<SessionStateConfig>(sessEl.GetRawText(), UserSettings.JSONOptions) ?? new() :
                          new()
        };
    }
}
