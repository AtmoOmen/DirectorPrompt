using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Infrastructure.Security;
using Serilog;

namespace DirectorPrompt.Infrastructure;

public sealed class UserSettingsStore : IUserSettingsStore
{
    private const string SECRET_MANIFEST_KEY = "user-settings-secret-manifest-v1";

    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly ISecretStore secretStore;
    private readonly string userSettingsPath;

    public UserSettingsStore(string? userSettingsPath = null, ISecretStore? secretStore = null)
    {
        this.userSettingsPath = userSettingsPath ?? AppPaths.UserSettingsPath;
        this.secretStore      = secretStore ?? new SecretStore();
    }

    public UserSettings Load()
    {
        var migrated = MigrateIfNeeded();

        if (!File.Exists(userSettingsPath))
        {
            Log.Information("用户设置文件不存在, 使用默认设置: 路径={UserSettingsPath}", userSettingsPath);
            return new UserSettings();
        }

        var json     = File.ReadAllText(userSettingsPath, Utf8);
        var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions.Default) ?? new UserSettings();

        HydrateSecrets(settings);

        Log.Information
        (
            "用户设置已加载: 路径={UserSettingsPath}, 已迁移={Migrated}, 提供商数={ProviderCount}, 模型数={ModelCount}, Agent任务数={AgentTaskCount}, MCP服务数={MCPServerCount}",
            userSettingsPath,
            migrated,
            settings.Orchestrator.Providers.Count,
            settings.Orchestrator.Models.Count,
            settings.Orchestrator.AgentTasks.Count,
            settings.MCPServers.Count
        );

        return settings;
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(userSettingsPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        try
        {
            await Task.Run(() => SynchronizeSecrets(settings), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var json = JsonSerializer.Serialize(settings, JsonOptions.Default);

            await File.WriteAllTextAsync(userSettingsPath, json, Utf8, cancellationToken);

            Log.Information
            (
                "用户设置已保存: 路径={UserSettingsPath}, 字节数={ByteCount}, 提供商数={ProviderCount}, 模型数={ModelCount}, Agent任务数={AgentTaskCount}, MCP服务数={MCPServerCount}",
                userSettingsPath,
                Utf8.GetByteCount(json),
                settings.Orchestrator.Providers.Count,
                settings.Orchestrator.Models.Count,
                settings.Orchestrator.AgentTasks.Count,
                settings.MCPServers.Count
            );
        }
        catch (Exception exception)
        {
            Log.Error(exception, "保存用户设置失败: 路径={UserSettingsPath}", userSettingsPath);
            throw;
        }
    }

    public bool MigrateIfNeeded()
    {
        if (!File.Exists(userSettingsPath))
            return false;

        var json = File.ReadAllText(userSettingsPath, Utf8);

        using var doc = JsonDocument.Parse(json);

        if (!TryGetProperty(doc.RootElement, "Orchestrator", out var orchestrator))
            return false;

        if (!TryGetProperty(orchestrator, "Providers", out _) &&
            TryGetProperty(orchestrator, "Agents", out var agents) &&
            agents.ValueKind == JsonValueKind.Array)
        {
            Log.Information("检测到旧版用户设置, 开始迁移: 路径={UserSettingsPath}", userSettingsPath);

            var legacySettings = MigrateFromLegacy(doc.RootElement);

            _ = MigratePlaintextSecrets(legacySettings);
            WriteSettings(legacySettings);

            Log.Information("旧版用户设置迁移完成: 路径={UserSettingsPath}", userSettingsPath);
            return true;
        }

        var root = JsonNode.Parse(json)?.AsObject();

        if (root is null)
            return false;

        var removedCount = RemoveObsoleteAgentTasks(root);
        var settingsJSON  = removedCount > 0 ? root.ToJsonString(JsonOptions.Default) : json;
        var settings = JsonSerializer.Deserialize<UserSettings>(settingsJSON, JsonOptions.Default) ?? new UserSettings();
        var secretsMigrated = MigratePlaintextSecrets(settings);

        if (removedCount > 0 || secretsMigrated)
        {
            WriteSettings(settings);

            if (removedCount > 0)
            {
                Log.Information
                (
                    "已清理废弃 Agent 任务配置: 路径={UserSettingsPath}, 删除数量={RemovedCount}",
                    userSettingsPath,
                    removedCount
                );
            }
        }

        return removedCount > 0 || secretsMigrated;
    }

    private static UserSettings MigrateFromLegacy(JsonElement root)
    {
        var providers  = new List<ProviderConfig>();
        var models     = new List<ModelConfig>();
        var prompts    = new List<PromptConfig>();
        var agentTasks = new List<AgentTaskConfig>();

        var orchestrator = root.GetProperty("Orchestrator");
        var agents       = orchestrator.GetProperty("Agents").EnumerateArray().ToList();
        var providerCache = new Dictionary<string, ProviderConfig>();

        foreach (var agent in agents)
        {
            var role = agent.GetProperty("Role").GetString() ?? "Narrator";
            var modelConfig = agent.GetProperty("ModelConfig");
            var systemPrompt = agent.TryGetProperty("SystemPrompt", out var systemPromptElement) ?
                                   systemPromptElement.GetString() :
                                   null;
            var temperature = agent.TryGetProperty("Temperature", out var temperatureElement) ?
                                  temperatureElement.GetSingle() :
                                  0.8f;

            var providerType = modelConfig.GetProperty("Provider").GetString() ?? "openai";
            var endpoint     = modelConfig.GetProperty("Endpoint").GetString() ?? string.Empty;
            var apiKey = modelConfig.TryGetProperty("APIKey", out var apiKeyElement) ?
                             apiKeyElement.GetString() :
                             null;
            var modelName = modelConfig.GetProperty("ModelName").GetString() ?? string.Empty;
            var providerKey = $"{providerType}|{endpoint}|{apiKey}";

            if (!providerCache.TryGetValue(providerKey, out var provider))
            {
                provider = new ProviderConfig
                {
                    DisplayName = providerType.Equals("openai", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(endpoint) ?
                                      "OpenAI" :
                                      providerType,
                    Provider = providerType,
                    Endpoint = endpoint,
                    APIKey   = apiKey
                };

                providerCache[providerKey] = provider;
                providers.Add(provider);
            }

            string? promptID = null;

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                var prompt = new PromptConfig
                {
                    DisplayName = $"{role} 提示词",
                    Content     = systemPrompt
                };

                prompts.Add(prompt);
                promptID = prompt.ID;
            }

            var model = new ModelConfig
            {
                DisplayName     = $"{role} - {modelName}",
                ProviderID      = provider.ID,
                ModelName       = modelName,
                Temperature     = temperature,
                ReasoningEffort = null,
                PromptID        = promptID
            };

            models.Add(model);

            var taskType = role switch
            {
                "Narrator"          => AgentTaskType.Narrator,
                "Memory" or "State" => AgentTaskType.MemoryUpdate,
                "Scene"             => AgentTaskType.Scene,
                _                   => (AgentTaskType?)null
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

        if (root.TryGetProperty("EmbeddingConfig", out var embeddingElement))
        {
            var embeddingProvider = embeddingElement.TryGetProperty("Provider", out var providerElement) ?
                                        providerElement.GetString() ?? "openai" :
                                        "openai";
            var embeddingEndpoint = embeddingElement.TryGetProperty("Endpoint", out var endpointElement) ?
                                        endpointElement.GetString() ?? string.Empty :
                                        string.Empty;
            var embeddingAPIKey = embeddingElement.TryGetProperty("APIKey", out var apiKeyElement) ?
                                      apiKeyElement.GetString() :
                                      null;
            var embeddingModel = embeddingElement.TryGetProperty("ModelName", out var modelElement) ?
                                     modelElement.GetString() ?? "text-embedding-v4" :
                                     "text-embedding-v4";
            var embeddingProviderKey = $"{embeddingProvider}|{embeddingEndpoint}|{embeddingAPIKey}";

            if (!providerCache.TryGetValue(embeddingProviderKey, out var embeddingProviderConfig))
            {
                embeddingProviderConfig = new ProviderConfig
                {
                    DisplayName = "Embedding Provider",
                    Provider    = embeddingProvider,
                    Endpoint    = embeddingEndpoint,
                    APIKey      = embeddingAPIKey
                };

                providerCache[embeddingProviderKey] = embeddingProviderConfig;
                providers.Add(embeddingProviderConfig);
            }

            embeddingConfig = new EmbeddingConfig
            {
                ProviderID = embeddingProviderConfig.ID,
                ModelName  = embeddingModel
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
            Localization = root.TryGetProperty("Localization", out var localizationElement) ?
                               JsonSerializer.Deserialize<LocalizationConfig>(localizationElement.GetRawText(), JsonOptions.Default) ?? new() :
                               new(),
            Session = root.TryGetProperty("Session", out var sessionElement) ?
                          JsonSerializer.Deserialize<SessionStateConfig>(sessionElement.GetRawText(), JsonOptions.Default) ?? new() :
                          new()
        };
    }

    private void SynchronizeSecrets(UserSettings settings)
    {
        var entries = GetSecretEntries(settings).ToList();
        var current = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var value = entry.GetValue();

            if (string.IsNullOrEmpty(value))
            {
                secretStore.Remove(entry.Key);
                continue;
            }

            secretStore.Set(entry.Key, value);
            current.Add(entry.Key);
        }

        var previous = ReadSecretManifest();

        foreach (var key in previous.Except(current))
            secretStore.Remove(key);

        WriteSecretManifest(current);
    }

    private bool MigratePlaintextSecrets(UserSettings settings)
    {
        var plaintext = GetSecretEntries(settings)
                        .Where(entry => !string.IsNullOrEmpty(entry.GetValue()))
                        .ToList();

        if (plaintext.Count == 0)
            return false;

        foreach (var entry in plaintext)
            secretStore.Set(entry.Key, entry.GetValue()!);

        var manifest = ReadSecretManifest();

        manifest.UnionWith(plaintext.Select(entry => entry.Key));
        WriteSecretManifest(manifest);

        Log.Information("已将明文设置迁移到系统凭据存储: 数量={SecretCount}", plaintext.Count);
        return true;
    }

    private void HydrateSecrets(UserSettings settings)
    {
        var manifest = ReadSecretManifest();

        if (manifest.Count == 0)
            return;

        foreach (var entry in GetSecretEntries(settings))
        {
            if (!manifest.Contains(entry.Key))
                continue;

            entry.SetValue(secretStore.Get(entry.Key) ?? string.Empty);
        }
    }

    private HashSet<string> ReadSecretManifest()
    {
        var json = secretStore.Get(SECRET_MANIFEST_KEY);

        if (string.IsNullOrWhiteSpace(json))
            return new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var keys = JsonSerializer.Deserialize<List<string>>(json, JsonOptions.Default) ?? [];

            return keys.Where(key => !string.IsNullOrWhiteSpace(key))
                       .ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            Log.Warning(exception, "安全凭据清单无效, 将在下次保存时重建");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private void WriteSecretManifest(IReadOnlySet<string> keys)
    {
        if (keys.Count == 0)
        {
            secretStore.Remove(SECRET_MANIFEST_KEY);
            return;
        }

        var json = JsonSerializer.Serialize
        (
            keys.OrderBy(key => key, StringComparer.Ordinal).ToList(),
            JsonOptions.Compact
        );

        secretStore.Set(SECRET_MANIFEST_KEY, json);
    }

    private void WriteSettings(UserSettings settings)
    {
        var directory = Path.GetDirectoryName(userSettingsPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions.Default);

        File.WriteAllText(userSettingsPath, json, Utf8);
    }

    private static IEnumerable<SecretEntry> GetSecretEntries(UserSettings settings)
    {
        foreach (var provider in settings.Orchestrator.Providers)
        {
            yield return new SecretEntry
            (
                CreateSecretKey("provider", provider.ID, "api-key"),
                () => provider.APIKey,
                value => provider.APIKey = value
            );
            yield return new SecretEntry
            (
                CreateSecretKey("provider", provider.ID, "custom-headers"),
                () => provider.CustomHeaders,
                value => provider.CustomHeaders = value
            );
        }

        foreach (var server in settings.MCPServers)
        {
            for (var index = 0; index < server.Arguments.Count; index++)
            {
                var argumentIndex = index;

                yield return new SecretEntry
                (
                    CreateSecretKey("mcp", server.ID, "argument", argumentIndex.ToString()),
                    () => server.Arguments[argumentIndex],
                    value => server.Arguments[argumentIndex] = value ?? string.Empty
                );
            }

            foreach (var name in server.Environment.Keys.ToList())
            {
                var environmentName = name;

                yield return new SecretEntry
                (
                    CreateSecretKey("mcp", server.ID, "environment", environmentName),
                    () => server.Environment[environmentName],
                    value => server.Environment[environmentName] = value ?? string.Empty
                );
            }

            foreach (var name in server.Headers.Keys.ToList())
            {
                var headerName = name;

                yield return new SecretEntry
                (
                    CreateSecretKey("mcp", server.ID, "header", headerName),
                    () => server.Headers[headerName],
                    value => server.Headers[headerName] = value ?? string.Empty
                );
            }
        }
    }

    private static string CreateSecretKey(params string[] parts) =>
        string.Join('\0', parts);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static int RemoveObsoleteAgentTasks(JsonObject root)
    {
        var orchestrator = root["Orchestrator"]?.AsObject() ?? root["orchestrator"]?.AsObject();
        var tasks = orchestrator?["AgentTasks"]?.AsArray() ?? orchestrator?["agentTasks"]?.AsArray();

        if (tasks is null)
            return 0;

        var removedCount = 0;

        for (var index = tasks.Count - 1; index >= 0; index--)
        {
            var task = tasks[index]?.AsObject();
            var taskType = task?["TaskType"]?.GetValue<string>() ?? task?["taskType"]?.GetValue<string>();

            if (taskType is not ("Knowledge" or "MemoryRecall"))
                continue;

            tasks.RemoveAt(index);
            removedCount++;
        }

        return removedCount;
    }

    private sealed record SecretEntry
    (
        string Key,
        Func<string?> GetValue,
        Action<string?> SetValue
    );
}
