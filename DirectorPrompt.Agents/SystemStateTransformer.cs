using System.Collections.Concurrent;
using System.Text.Json;
using System.Globalization;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Configurations;
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
    IExpressionEngine    expressionEngine
) : ISystemStateTransformer
{
    private readonly ConcurrentDictionary<(long SessionID, long AttributeID, long? CharacterID, string Option), bool> onceTriggered = new();
    private readonly ConcurrentDictionary<(long SessionID, long AttributeID, long? CharacterID, string RuleID), bool> numericOnceTriggered = new();

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
            Log.Debug("无系统驱动的状态属性, 跳过");
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
                {
                    await TransformCategoryAttributeAsync
                        (attr, sceneCharacters, sessionID, sceneID.Value, roundID, trigger, attrNameCache, globalStateValues, cancellationToken);
                }
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
        var value        = await stateRepository.GetStateValueAsync(attr.ID, sessionID, cancellationToken);
        var currentValue = value?.Value ?? "0";

        if (attr.ValueType == StateValueType.Numeric)
        {
            await TransformNumericAttributeAsync
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
            return;
        }

        if (attr.ValueType != StateValueType.Enum)
            return;

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
        if (attr.ValueType is not (StateValueType.Enum or StateValueType.Numeric))
            return;

        if (characters.Count == 0)
            return;

        var characterIDs   = characters.Select(c => c.ID).ToList();
        var allStateValues = await characterRepository.GetCharacterStateValuesBatchAsync(characterIDs, cancellationToken);
        var valuesByChar = allStateValues.GroupBy(v => v.CharacterID)
                                         .ToDictionary(g => g.Key);

        foreach (var character in characters)
        {
            var charValues = valuesByChar.TryGetValue(character.ID, out var vals) ?
                                 vals.ToList() :
                                 [];

            var charContext = charValues.ToDictionary
            (
                v => attrNameCache.TryGetValue(v.AttributeID, out var name) ?
                         name :
                         v.AttributeID.ToString(),
                v => v.Value
            );

            var currentValue = charValues.FirstOrDefault(v => v.AttributeID == attr.ID)?.Value ?? "0";

            if (attr.ValueType == StateValueType.Numeric)
            {
                await TransformNumericAttributeAsync
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
            else
            {
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
        }
    }

    private async Task TransformNumericAttributeAsync
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
        var config = string.IsNullOrWhiteSpace(attr.Config) ?
                         null :
                         JsonSerializer.Deserialize<StateAttributeConfig>(attr.Config, JsonOptions.Default);

        if (config?.NumericChanges.Count is not > 0 ||
            !float.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var initialValue) ||
            !float.IsFinite(initialValue))
            return;

        var newValue = initialValue;

        for (var index = 0; index < config.NumericChanges.Count; index++)
        {
            var change = config.NumericChanges[index];

            if (!IsTriggerMatch(change.Trigger, trigger))
                continue;

            var ruleID = string.IsNullOrWhiteSpace(change.ID) ?
                             index.ToString(CultureInfo.InvariantCulture) :
                             change.ID;
            var key = (sessionID, attr.ID, characterID, ruleID);
            var isMet = EvaluateNumericChangeCondition(change, stateValues, newValue);

            if (!isMet)
            {
                numericOnceTriggered[key] = false;
                continue;
            }

            if (change.SwitchMode == EnumSwitchMode.Once &&
                numericOnceTriggered.TryGetValue(key, out var triggered) && triggered)
                continue;

            try
            {
                newValue = expressionEngine.EvaluateNumeric
                           (
                               change.ChangeExpression,
                               newValue.ToString(CultureInfo.InvariantCulture)
                           );
                newValue = ClampNumericValue(newValue, config);
                numericOnceTriggered[key] = change.SwitchMode == EnumSwitchMode.Once;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "数值变更式求值失败: {Expression}", change.ChangeExpression);
            }
        }

        var formattedValue = newValue.ToString(CultureInfo.InvariantCulture);

        if (formattedValue == currentValue)
            return;

        if (characterID is not null)
            await characterRepository.SetCharacterStateValueAsync(characterID.Value, attr.ID, formattedValue, sessionID, roundID, cancellationToken);
        else
        {
            await stateRepository.SetStateValueAsync
            (
                attr.ID,
                sessionID,
                formattedValue,
                StateChangeSource.System,
                $"system 数值变更: {currentValue} → {formattedValue}",
                sceneID ?? 0,
                roundID,
                cancellationToken
            );
        }

        stateValues[attr.Name] = formattedValue;
    }

    private static float ClampNumericValue(float value, StateAttributeConfig config)
    {
        if (config.Min is not null && value < config.Min)
            value = config.Min.Value;

        if (config.Max is not null && value > config.Max)
            value = config.Max.Value;

        return value;
    }

    private bool EvaluateNumericChangeCondition
    (
        NumericStateChangeRuleConfig change,
        Dictionary<string, string>    stateValues,
        float                          currentValue
    )
    {
        var value = currentValue.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(change.AttributeName) &&
            !stateValues.TryGetValue(change.AttributeName, out value))
            return false;

        try
        {
            return expressionEngine.Evaluate(change.Expression, value);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "数值变更条件求值失败: {Expression}", change.Expression);
            return false;
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
        var config = string.IsNullOrWhiteSpace(attr.Config) ?
                         null :
                         JsonSerializer.Deserialize<StateAttributeConfig>(attr.Config, JsonOptions.Default);

        if (config is null)
            return;

        if (!IsTriggerMatch(ParseTrigger(config.Trigger), trigger))
            return;

        if (string.IsNullOrEmpty(currentValue))
            currentValue = config.Options.FirstOrDefault() ?? string.Empty;

        var newValue = ResolveEnumTransition(attr, sessionID, characterID, currentValue, config, stateValues);

        if (newValue == currentValue)
            return;

        if (characterID is not null)
            await characterRepository.SetCharacterStateValueAsync(characterID.Value, attr.ID, newValue, sessionID, roundID, cancellationToken);
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

    private string ResolveEnumTransition
    (
        StateAttribute             attr,
        long                       sessionID,
        long?                      characterID,
        string                     currentValue,
        StateAttributeConfig       config,
        Dictionary<string, string> stateValues
    )
    {
        if (config.Transitions.Count == 0)
            return currentValue;

        var alwaysMet = new List<(string Option, float Weight)>();

        foreach (var t in config.Transitions)
        {
            if (t.Method != EnumTransitionMethod.Expression || t.SwitchMode != EnumSwitchMode.Always)
                continue;

            if (EvaluateTransitionExpression(t, stateValues))
                alwaysMet.Add((t.Option, t.Weight));
        }

        if (alwaysMet.Count > 0)
        {
            var best = alwaysMet.MaxBy(x => x.Weight);
            return best.Option;
        }

        var onceFirstTime = new List<(string Option, float Weight)>();

        foreach (var t in config.Transitions)
        {
            if (t.Method != EnumTransitionMethod.Expression || t.SwitchMode != EnumSwitchMode.Once)
                continue;

            var key   = (sessionID, attr.ID, characterID, t.Option);
            var isMet = EvaluateTransitionExpression(t, stateValues);

            if (!isMet)
            {
                onceTriggered[key] = false;
                continue;
            }

            if (onceTriggered.TryGetValue(key, out var triggered) && triggered)
                continue;

            onceTriggered[key] = true;
            onceFirstTime.Add((t.Option, t.Weight));
        }

        if (onceFirstTime.Count > 0)
        {
            var best = onceFirstTime.MaxBy(x => x.Weight);
            return best.Option;
        }

        var pool = new List<(string Option, float Weight)>();

        foreach (var t in config.Transitions)
        {
            switch (t.Method)
            {
                case EnumTransitionMethod.Random:
                    pool.Add((t.Option, t.Weight));
                    break;

                case EnumTransitionMethod.Expression when t.SwitchMode == EnumSwitchMode.Once:
                {
                    var key   = (sessionID, attr.ID, characterID, t.Option);
                    var isMet = EvaluateTransitionExpression(t, stateValues);

                    if (isMet && onceTriggered.TryGetValue(key, out var triggered) && triggered)
                        pool.Add((t.Option, t.Weight));

                    break;
                }
            }
        }

        if (pool.Count > 0)
            return PickWeighted(pool);

        return currentValue;
    }

    private bool EvaluateTransitionExpression
    (
        EnumTransitionConfig       transition,
        Dictionary<string, string> stateValues
    )
    {
        if (string.IsNullOrWhiteSpace(transition.Expression) || string.IsNullOrWhiteSpace(transition.AttributeName))
            return false;

        if (!stateValues.TryGetValue(transition.AttributeName, out var value))
            return false;

        try
        {
            return expressionEngine.Evaluate(transition.Expression, value);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "表达式求值失败: {Expression}", transition.Expression);
            return false;
        }
    }

    private static string PickWeighted(List<(string Option, float Weight)> pool)
    {
        var total = pool.Sum(x => x.Weight);

        if (total <= 0)
            return pool[0].Option;

        var roll       = (float)Random.Shared.NextDouble() * total;
        var cumulative = 0f;

        foreach (var (option, weight) in pool)
        {
            cumulative += weight;

            if (roll <= cumulative)
                return option;
        }

        return pool[^1].Option;
    }

    private async Task<Dictionary<string, string>> BuildGlobalStateContextAsync
    (
        IReadOnlyList<StateAttribute> allAttributes,
        long                          sessionID,
        CancellationToken             cancellationToken
    )
    {
        var result = new Dictionary<string, string>();

        var globalAttrs = allAttributes.Where(a => a.Scope == StateScope.Global).ToList();

        if (globalAttrs.Count == 0)
            return result;

        var attrIDs     = globalAttrs.Select(a => a.ID).ToList();
        var stateValues = await stateRepository.GetStateValuesAsync(attrIDs, sessionID, cancellationToken);
        var valueMap    = stateValues.ToDictionary(v => v.AttributeID);

        foreach (var attr in globalAttrs)
        {
            result[attr.Name] = valueMap.TryGetValue(attr.ID, out var sv) ?
                                    sv.Value :
                                    string.Empty;
        }

        return result;
    }

    private static bool IsTriggerMatch(SystemTrigger configTrigger, SystemTrigger actualTrigger) =>
        configTrigger == actualTrigger;

    private static SystemTrigger ParseTrigger(string? value) =>
        Enum.TryParse(value, true, out SystemTrigger trigger) ?
            trigger :
            SystemTrigger.RoundEnd;
}
