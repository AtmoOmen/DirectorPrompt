using System.Text.Json;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Serilog;

namespace DirectorPrompt.Agents;

public sealed class SystemStateTransformer
(
    IStateRepository     stateRepository,
    ICharacterRepository characterRepository,
    IConditionEngine     conditionEngine
) : ISystemStateTransformer
{
    public async Task ExecuteAsync
    (
        long              projectID,
        long              sessionID,
        long?             sceneID,
        long              roundID,
        SystemTrigger     trigger,
        CancellationToken cancellationToken = default
    )
    {
        Log.Information
        (
            "系统状态变换开始: project={ProjectID}, session={SessionID}, trigger={Trigger}",
            projectID,
            sessionID,
            trigger
        );

        var attributes  = await stateRepository.GetAttributesAsync(projectID, null, cancellationToken);
        var systemAttrs = attributes.Where(a => a.Driver == Driver.System).ToList();

        if (systemAttrs.Count == 0)
        {
            Log.Debug("无 system 驱动的状态属性, 跳过");
            return;
        }

        var globalStateValues = await BuildGlobalStateContextAsync(attributes, sessionID, cancellationToken);
        var attrNameCache     = attributes.ToDictionary(a => a.ID, a => a.Name);

        foreach (var attr in systemAttrs)
        {
            if (attr.Scope == StateScope.Global)
                await TransformGlobalAttributeAsync(attr, sessionID, sceneID, roundID, trigger, globalStateValues, cancellationToken);
        }

        if (sceneID is not null)
        {
            var sceneCharacters = await characterRepository.GetBySceneAsync(sceneID.Value, cancellationToken);

            foreach (var attr in systemAttrs)
            {
                if (attr.Scope == StateScope.Category)
                    await TransformCategoryAttributeAsync
                        (attr, sceneCharacters, sessionID, sceneID.Value, roundID, trigger, attrNameCache, globalStateValues, cancellationToken);
            }
        }

        Log.Information("系统状态变换完成");
    }

    private async Task TransformGlobalAttributeAsync
    (
        StateAttribute             attr,
        long                       sessionID,
        long?                      sceneID,
        long                       roundID,
        SystemTrigger              trigger,
        Dictionary<string, string> globalStateValues,
        CancellationToken          cancellationToken
    )
    {
        if (attr.ValueType == StateValueType.Enum)
        {
            var value        = await stateRepository.GetStateValueAsync(attr.ID, sessionID, cancellationToken);
            var currentValue = value?.Value ?? string.Empty;

            await TransformEnumAttributeAsync
            (
                attr,
                sessionID,
                sceneID,
                roundID,
                trigger,
                currentValue,
                globalStateValues,
                null,
                cancellationToken
            );
        }
        else if (attr.ValueType == StateValueType.Composite)
        {
            await TransformCompositeAttributeAsync
            (
                attr,
                sessionID,
                trigger,
                globalStateValues,
                cancellationToken
            );
        }
    }

    private async Task TransformCategoryAttributeAsync
    (
        StateAttribute             attr,
        IReadOnlyList<Character>   characters,
        long                       sessionID,
        long                       sceneID,
        long                       roundID,
        SystemTrigger              trigger,
        Dictionary<long, string>   attrNameCache,
        Dictionary<string, string> globalStateValues,
        CancellationToken          cancellationToken
    )
    {
        foreach (var character in characters)
        {
            var charValues = await characterRepository.GetCharacterStateValuesAsync(character.ID, cancellationToken);
            var charContext = charValues.ToDictionary
            (
                v => attrNameCache.TryGetValue(v.AttributeID, out var name) ?
                         name :
                         v.AttributeID.ToString(),
                v => v.Value
            );

            if (attr.ValueType == StateValueType.Enum)
            {
                var currentValue = charValues.FirstOrDefault(v => v.AttributeID == attr.ID)?.Value ?? string.Empty;

                await TransformEnumAttributeAsync
                (
                    attr,
                    sessionID,
                    sceneID,
                    roundID,
                    trigger,
                    currentValue,
                    charContext,
                    character.ID,
                    cancellationToken
                );
            }
            else if (attr.ValueType == StateValueType.Composite)
            {
                await TransformCompositeAttributeAsync
                (
                    attr,
                    sessionID,
                    trigger,
                    globalStateValues,
                    cancellationToken
                );
            }
        }
    }

    private async Task TransformEnumAttributeAsync
    (
        StateAttribute             attr,
        long                       sessionID,
        long?                      sceneID,
        long                       roundID,
        SystemTrigger              trigger,
        string                     currentValue,
        Dictionary<string, string> stateValues,
        long?                      characterID,
        CancellationToken          cancellationToken
    )
    {
        var config = ParseEnumConfig(attr.Config);

        if (config is null)
            return;

        if (!IsTriggerMatch(config.Trigger, trigger))
            return;

        if (string.IsNullOrEmpty(currentValue))
            currentValue = config.Options.FirstOrDefault() ?? string.Empty;

        var newValue = ResolveEnumTransition(currentValue, config, stateValues);

        if (newValue == currentValue)
            return;

        if (characterID is not null)
            await characterRepository.SetCharacterStateValueAsync(characterID.Value, attr.ID, newValue, cancellationToken);
        else
        {
            await stateRepository.SetStateValueAsync
            (
                attr.ID,
                sessionID,
                newValue,
                StateChangeSource.System,
                $"system 变换: {currentValue} → {newValue}",
                sceneID ?? 0,
                roundID,
                cancellationToken
            );
        }

        Log.Information
        (
            "状态变换: {AttrName} {OldValue} → {NewValue} (character={CharacterID})",
            attr.Name,
            currentValue,
            newValue,
            characterID
        );
    }

    private async Task TransformCompositeAttributeAsync
    (
        StateAttribute             attr,
        long                       sessionID,
        SystemTrigger              trigger,
        Dictionary<string, string> globalStateValues,
        CancellationToken          cancellationToken
    )
    {
        var config = ParseCompositeConfig(attr.Config);

        if (config is null)
            return;

        if (config.RegenerateTrigger.HasValue && IsTriggerMatch(config.RegenerateTrigger.Value, trigger))
        {
            var shouldRegenerate = true;

            if (!string.IsNullOrWhiteSpace(config.RegenerateCondition))
            {
                var context = new ConditionContext(globalStateValues);
                shouldRegenerate = conditionEngine.Evaluate(config.RegenerateCondition, context);
            }

            if (shouldRegenerate)
            {
                Log.Information
                (
                    "composite 属性 {AttrName} 触发重新生成 (AI 生成尚未实现, 跳过)",
                    attr.Name
                );
            }
        }

        var items = await stateRepository.GetCompositeItemsAsync(attr.ID, sessionID, cancellationToken);

        foreach (var item in items)
        {
            if (item.Status != CompositeItemStatus.Active)
                continue;

            if (item.Current >= item.Target && item.Target > 0)
                await stateRepository.UpdateCompositeItemAsync(item.ID, null, item.Target, "system 自动完成", cancellationToken);
        }
    }

    private string ResolveEnumTransition
    (
        string                     currentValue,
        EnumConfig                 config,
        Dictionary<string, string> stateValues
    )
    {
        if (config.Conditions.Count > 0)
        {
            var context = new ConditionContext(stateValues);

            foreach (var cond in config.Conditions)
            {
                if (conditionEngine.Evaluate(cond.When, context))
                    return PickWeighted(cond.Transition);
            }
        }

        if (config.TransitionRules.TryGetValue(currentValue, out var rules))
            return PickWeighted(rules);

        return currentValue;
    }

    private static string PickWeighted(Dictionary<string, float> weights)
    {
        var total = weights.Values.Sum();

        if (total <= 0)
            return weights.Keys.FirstOrDefault() ?? string.Empty;

        var roll       = (float)Random.Shared.NextDouble() * total;
        var cumulative = 0f;

        foreach (var (key, weight) in weights)
        {
            cumulative += weight;

            if (roll <= cumulative)
                return key;
        }

        return weights.Keys.Last();
    }

    private async Task<Dictionary<string, string>> BuildGlobalStateContextAsync
    (
        IReadOnlyList<StateAttribute> allAttributes,
        long                          sessionID,
        CancellationToken             cancellationToken
    )
    {
        var result = new Dictionary<string, string>();

        foreach (var attr in allAttributes.Where(a => a.Scope == StateScope.Global))
        {
            var value = await stateRepository.GetStateValueAsync(attr.ID, sessionID, cancellationToken);
            result[attr.Name] = value?.Value ?? string.Empty;
        }

        return result;
    }

    private static bool IsTriggerMatch(SystemTrigger configTrigger, SystemTrigger actualTrigger) =>
        configTrigger == actualTrigger;

    private static EnumConfig? ParseEnumConfig(string json)
    {
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;

            var config = new EnumConfig
            {
                Options = root.TryGetProperty("options", out var optionsEl) ?
                              optionsEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList() :
                              new List<string>(),
                Trigger = root.TryGetProperty("trigger", out var triggerEl) ?
                              ParseTrigger(triggerEl.GetString()) :
                              SystemTrigger.RoundEnd
            };

            if (root.TryGetProperty("transitionRules", out var rulesEl))
            {
                foreach (var fromProp in rulesEl.EnumerateObject())
                {
                    var transitions = new Dictionary<string, float>();

                    foreach (var toProp in fromProp.Value.EnumerateObject())
                        transitions[toProp.Name] = toProp.Value.GetSingle();

                    config.TransitionRules[fromProp.Name] = transitions;
                }
            }

            if (root.TryGetProperty("conditions", out var condsEl))
            {
                foreach (var condEl in condsEl.EnumerateArray())
                {
                    var when        = condEl.GetProperty("when").GetString() ?? string.Empty;
                    var transitions = new Dictionary<string, float>();

                    if (condEl.TryGetProperty("transition", out var transEl))
                    {
                        foreach (var prop in transEl.EnumerateObject())
                            transitions[prop.Name] = prop.Value.GetSingle();
                    }

                    config.Conditions.Add(new EnumCondition { When = when, Transition = transitions });
                }
            }

            return config;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "解析 enum 配置失败");
            return null;
        }
    }

    private static CompositeConfig? ParseCompositeConfig(string json)
    {
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;

            var config = new CompositeConfig
            {
                GenerationGuide = root.TryGetProperty("generationGuide", out var guideEl) ?
                                      guideEl.GetString() ?? string.Empty :
                                      string.Empty
            };

            if (root.TryGetProperty("regenerateTrigger", out var triggerEl))
                config.RegenerateTrigger = ParseTrigger(triggerEl.GetString());

            if (root.TryGetProperty("regenerateCondition", out var condEl))
                config.RegenerateCondition = condEl.GetString();

            return config;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "解析 composite 配置失败");
            return null;
        }
    }

    private static SystemTrigger ParseTrigger(string? value) =>
        value switch
        {
            "scene_change" => SystemTrigger.SceneChange,
            "round_end"    => SystemTrigger.RoundEnd,
            "custom"       => SystemTrigger.Custom,
            _              => SystemTrigger.RoundEnd
        };

    private sealed class EnumConfig
    {
        public List<string>                                  Options         { get; set; } = [];
        public Dictionary<string, Dictionary<string, float>> TransitionRules { get; set; } = [];
        public List<EnumCondition>                           Conditions      { get; set; } = [];
        public SystemTrigger                                 Trigger         { get; set; } = SystemTrigger.RoundEnd;
    }

    private sealed class EnumCondition
    {
        public string                    When       { get; set; } = string.Empty;
        public Dictionary<string, float> Transition { get; set; } = [];
    }

    private sealed class CompositeConfig
    {
        public string         GenerationGuide     { get; set; } = string.Empty;
        public SystemTrigger? RegenerateTrigger   { get; set; }
        public string?        RegenerateCondition { get; set; }
    }
}
