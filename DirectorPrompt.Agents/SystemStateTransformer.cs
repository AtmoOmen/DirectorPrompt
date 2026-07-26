using System.Globalization;
using System.Text.Json;
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
    IStateRuleExecutionRepository stateRuleExecutionRepository,
    ICharacterRepository characterRepository,
    StateRuleEvaluator   stateRuleEvaluator
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
    ) =>
        await ExecuteAsync
        (
            projectID,
            sessionID,
            sceneID,
            roundID,
            new StateRuleEvent
            (
                trigger,
                $"{trigger}:{roundID}:{sceneID}",
                roundID,
                sceneID,
                null,
                null
            ),
            cancellationToken
        );

    public async Task ExecuteAsync
    (
        long              projectID,
        long              sessionID,
        long?             sceneID,
        long              roundID,
        StateRuleEvent    stateEvent,
        CancellationToken cancellationToken = default
    )
    {
        Log.Information
        (
            "系统状态变换开始: project={ProjectID}, session={SessionID}, trigger={Trigger}",
            projectID,
            sessionID,
            stateEvent.Trigger
        );

        var attributes  = await stateRepository.GetAttributesAsync(projectID, null, cancellationToken);
        var systemAttrs = attributes.Where(a => a.Driver == Driver.System).ToList();

        if (systemAttrs.Count == 0)
        {
            Log.Debug("无系统驱动的状态属性, 跳过");
            return;
        }

        var globalStateValues = await BuildGlobalStateContextAsync(attributes, sessionID, cancellationToken);
        var categories        = await characterRepository.GetCategoriesAsync(projectID, cancellationToken);
        var categoryNameCache = categories.ToDictionary(category => category.ID, category => category.Name);
        var characterStateNameCache = attributes
                                      .Where(attribute => attribute.CategoryID is not null)
                                      .ToDictionary
                                      (
                                          attribute => attribute.ID,
                                          attribute => $"{categoryNameCache.GetValueOrDefault(attribute.CategoryID!.Value, attribute.CategoryID.Value.ToString())}.{attribute.Name}"
                                      );

        foreach (var attr in systemAttrs)
        {
            if (attr.Scope == StateScope.Global)
                await TransformGlobalAttributeAsync(attr, sessionID, sceneID, roundID, stateEvent, globalStateValues, cancellationToken);
        }

        if (sceneID is not null)
        {
            var sceneCharacters = await characterRepository.GetBySceneAsync(sceneID.Value, cancellationToken);

            foreach (var attr in systemAttrs)
            {
                if (attr.Scope == StateScope.Category)
                {
                    await TransformCategoryAttributeAsync
                        (attr, sceneCharacters, sessionID, sceneID.Value, roundID, stateEvent, characterStateNameCache, globalStateValues, cancellationToken);
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
        StateRuleEvent             stateEvent,
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
                stateEvent,
                currentValue,
                globalStateValues,
                [],
                null,
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
            stateEvent,
            currentValue,
            globalStateValues,
            [],
            null,
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
        StateRuleEvent             stateEvent,
        Dictionary<long, string>   characterStateNameCache,
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
                v => characterStateNameCache.TryGetValue(v.AttributeID, out var name) ?
                         name :
                         v.AttributeID.ToString(),
                v => v.Value
            );
            var characterStateName = characterStateNameCache.GetValueOrDefault(attr.ID, attr.Name);

            var currentValue = charValues.FirstOrDefault(v => v.AttributeID == attr.ID)?.Value ?? "0";

            if (attr.ValueType == StateValueType.Numeric)
            {
                await TransformNumericAttributeAsync
                (
                    attr,
                    sessionID,
                    sceneID,
                    roundID,
                    stateEvent,
                    currentValue,
                    globalStateValues,
                    charContext,
                    character.ID,
                    characterStateName,
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
                    stateEvent,
                    currentValue,
                    globalStateValues,
                    charContext,
                    character.ID,
                    characterStateName,
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
        StateRuleEvent             stateEvent,
        string                     currentValue,
        Dictionary<string, string> globalStateValues,
        Dictionary<string, string> characterStateValues,
        long?                      characterID,
        string?                    characterStateName,
        CancellationToken          cancellationToken
    )
    {
        var config = string.IsNullOrWhiteSpace(attr.Config) ?
                         null :
                         JsonSerializer.Deserialize<StateAttributeConfig>(attr.Config, JsonOptions.Default);

        config = config is null ? null : StateRuleConfigMigration.Normalize(config, attr.Scope);

        if (config?.NumericChanges.Count is not > 0                                                               ||
            !float.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var initialValue) ||
            !float.IsFinite(initialValue))
            return;

        var newValue = initialValue;
        var firedRuleIDs = new List<string>();

        for (var index = 0; index < config.NumericChanges.Count; index++)
        {
            var change = config.NumericChanges[index];

            if (!IsTriggerMatch(change.Trigger, stateEvent.Trigger))
                continue;

            var ruleID = string.IsNullOrWhiteSpace(change.ID) ?
                             index.ToString(CultureInfo.InvariantCulture) :
                             change.ID;
            var isMet = stateRuleEvaluator.Matches
                        (
                            change.Conditions,
                            change.ConditionMatch,
                            newValue.ToString(CultureInfo.InvariantCulture),
                            globalStateValues,
                            characterStateValues,
                            stateEvent
                        );

            var repeatPolicy = change.RepeatPolicy ?? StateRuleRepeatPolicy.EveryEvent;

            if (!isMet)
            {
                await RecordRuleExecutionAsync
                (
                    sessionID,
                    attr.ID,
                    characterID,
                    ruleID,
                    stateEvent,
                    false,
                    false,
                    cancellationToken
                );
                continue;
            }

            if (!await IsRuleRepeatAllowedAsync
                       (
                           sessionID,
                           attr.ID,
                           characterID,
                           ruleID,
                           repeatPolicy,
                           stateEvent,
                           cancellationToken
                       ))
            {
                await RecordRuleExecutionAsync
                (
                    sessionID,
                    attr.ID,
                    characterID,
                    ruleID,
                    stateEvent,
                    true,
                    false,
                    cancellationToken
                );
                continue;
            }

            try
            {
                var operand = stateRuleEvaluator.EvaluateNumericValue
                              (
                                  change.ValueExpression ?? "0",
                                  newValue,
                                  globalStateValues,
                                  characterStateValues
                              );
                newValue = change.Operation switch
                {
                    NumericStateOperation.Add        => newValue + operand,
                    NumericStateOperation.Set        => operand,
                    NumericStateOperation.Multiply   => newValue * operand,
                    NumericStateOperation.Expression => operand,
                    _                                => newValue
                };
                newValue                  = ClampNumericValue(newValue, config);
                firedRuleIDs.Add(ruleID);
            }
            catch (Exception ex)
            {
                Log.Warning
                (
                    ex,
                    "数值变更式求值失败: {Expression}",
                    change.ValueExpression
                );
            }
        }

        var formattedValue = newValue.ToString(CultureInfo.InvariantCulture);

        if (formattedValue == currentValue)
        {
            foreach (var ruleID in firedRuleIDs)
            {
                await RecordRuleExecutionAsync
                (
                    sessionID,
                    attr.ID,
                    characterID,
                    ruleID,
                    stateEvent,
                    true,
                    true,
                    cancellationToken
                );
            }
            return;
        }

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

        if (characterID is null)
            globalStateValues[attr.Name] = formattedValue;
        else
            characterStateValues[characterStateName ?? attr.Name] = formattedValue;

        foreach (var ruleID in firedRuleIDs)
        {
            await RecordRuleExecutionAsync
            (
                sessionID,
                attr.ID,
                characterID,
                ruleID,
                stateEvent,
                true,
                true,
                cancellationToken
            );
        }
    }

    private async Task<bool> IsRuleRepeatAllowedAsync
    (
        long                  sessionID,
        long                  attributeID,
        long?                 characterID,
        string                ruleID,
        StateRuleRepeatPolicy repeatPolicy,
        StateRuleEvent        stateEvent,
        CancellationToken     cancellationToken
    )
    {
        var existing = await stateRuleExecutionRepository.GetByEventAsync
                       (
                           sessionID,
                           attributeID,
                           characterID,
                           ruleID,
                           stateEvent.EventKey,
                           cancellationToken
                       );

        if (existing is not null)
            return false;

        if (repeatPolicy == StateRuleRepeatPolicy.EveryEvent)
            return true;

        if (repeatPolicy == StateRuleRepeatPolicy.OnConditionEnter)
        {
            var latest = await stateRuleExecutionRepository.GetLatestAsync
                         (
                             sessionID,
                             attributeID,
                             characterID,
                             ruleID,
                             cancellationToken
                         );
            return latest?.ConditionMet != true;
        }

        long? roundID = repeatPolicy == StateRuleRepeatPolicy.OncePerRound ?
                            stateEvent.RoundID :
                            null;
        long? sceneID = repeatPolicy == StateRuleRepeatPolicy.OncePerScene ?
                            stateEvent.SceneID ?? 0 :
                            null;

        return !await stateRuleExecutionRepository.HasFiredAsync
                      (
                          sessionID,
                          attributeID,
                          characterID,
                          ruleID,
                          roundID,
                          sceneID,
                          cancellationToken
                      );
    }

    private Task RecordRuleExecutionAsync
    (
        long              sessionID,
        long              attributeID,
        long?             characterID,
        string            ruleID,
        StateRuleEvent    stateEvent,
        bool              conditionMet,
        bool              fired,
        CancellationToken cancellationToken
    ) =>
        stateRuleExecutionRepository.RecordAsync
        (
            new StateRuleExecution
            {
                SessionID    = sessionID,
                RoundID      = stateEvent.RoundID,
                SceneID      = stateEvent.SceneID ?? 0,
                AttributeID  = attributeID,
                CharacterID  = characterID ?? 0,
                RuleID       = ruleID,
                EventKey     = stateEvent.EventKey,
                ConditionMet = conditionMet,
                Fired        = fired
            },
            cancellationToken
        );

    private static float ClampNumericValue(float value, StateAttributeConfig config)
    {
        if (config.Min is not null && value < config.Min)
            value = config.Min.Value;

        if (config.Max is not null && value > config.Max)
            value = config.Max.Value;

        return value;
    }

    private async Task TransformEnumAttributeAsync
    (
        StateAttribute             attr,
        long                       sessionID,
        long?                      sceneID,
        long                       roundID,
        StateRuleEvent             stateEvent,
        string                     currentValue,
        Dictionary<string, string> globalStateValues,
        Dictionary<string, string> characterStateValues,
        long?                      characterID,
        string?                    characterStateName,
        CancellationToken          cancellationToken
    )
    {
        var config = string.IsNullOrWhiteSpace(attr.Config) ?
                         null :
                         JsonSerializer.Deserialize<StateAttributeConfig>(attr.Config, JsonOptions.Default);

        if (config is null)
            return;

        config = StateRuleConfigMigration.Normalize(config, attr.Scope);

        if (string.IsNullOrEmpty(currentValue))
            currentValue = config.Options.FirstOrDefault() ?? string.Empty;

        var selectedRule = await ResolveEnumTransitionWithRulesAsync
                           (
                               attr,
                               sessionID,
                               characterID,
                               currentValue,
                               config,
                               globalStateValues,
                               characterStateValues,
                               stateEvent,
                               cancellationToken
                           );
        var newValue = selectedRule?.Option ?? currentValue;

        if (newValue == currentValue)
        {
            if (selectedRule is not null)
            {
                await RecordRuleExecutionAsync
                (
                    sessionID,
                    attr.ID,
                    characterID,
                    GetTransitionRuleID(selectedRule),
                    stateEvent,
                    true,
                    true,
                    cancellationToken
                );
            }
            return;
        }

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

        if (characterID is null)
            globalStateValues[attr.Name] = newValue;
        else
            characterStateValues[characterStateName ?? attr.Name] = newValue;

        Log.Information
        (
            "状态变换: {AttrName} {OldValue} → {NewValue} (character={CharacterID})",
            attr.Name,
            currentValue,
            newValue,
            characterID
        );

        if (selectedRule is not null)
        {
            await RecordRuleExecutionAsync
            (
                sessionID,
                attr.ID,
                characterID,
                GetTransitionRuleID(selectedRule),
                stateEvent,
                true,
                true,
                cancellationToken
            );
        }
    }

    private async Task<EnumTransitionConfig?> ResolveEnumTransitionWithRulesAsync
    (
        StateAttribute                    attr,
        long                              sessionID,
        long?                             characterID,
        string                            currentValue,
        StateAttributeConfig              config,
        Dictionary<string, string>        globalStateValues,
        Dictionary<string, string>        characterStateValues,
        StateRuleEvent                    stateEvent,
        CancellationToken                 cancellationToken
    )
    {
        var candidates = new List<EnumTransitionConfig>();

        foreach (var transition in config.Transitions)
        {
            var trigger = transition.Trigger ?? ParseTrigger(config.Trigger);

            if (!IsTriggerMatch(trigger, stateEvent.Trigger))
                continue;

            var isMet = stateRuleEvaluator.Matches
                        (
                            transition.Conditions,
                            transition.ConditionMatch,
                            currentValue,
                            globalStateValues,
                            characterStateValues,
                            stateEvent
                        );

            var ruleID = GetTransitionRuleID(transition);

            if (!isMet)
            {
                await RecordRuleExecutionAsync
                (
                    sessionID,
                    attr.ID,
                    characterID,
                    ruleID,
                    stateEvent,
                    false,
                    false,
                    cancellationToken
                );
                continue;
            }

            var repeatPolicy = transition.RepeatPolicy ?? StateRuleRepeatPolicy.EveryEvent;

            if (!await IsRuleRepeatAllowedAsync
                       (
                           sessionID,
                           attr.ID,
                           characterID,
                           ruleID,
                           repeatPolicy,
                           stateEvent,
                           cancellationToken
                       ))
            {
                await RecordRuleExecutionAsync
                (
                    sessionID,
                    attr.ID,
                    characterID,
                    ruleID,
                    stateEvent,
                    true,
                    false,
                    cancellationToken
                );
                continue;
            }

            candidates.Add(transition);
        }

        if (candidates.Count == 0)
            return null;

        var highestPriority = candidates.Max(candidate => candidate.Priority);
        var highestPriorityCandidates = candidates
                                        .Where(candidate => candidate.Priority == highestPriority)
                                        .ToList();
        var selectedOption = highestPriorityCandidates.Count == 1 ?
                                 highestPriorityCandidates[0].Option :
                                 PickWeighted
                                 (
                                     highestPriorityCandidates
                                         .Select(candidate => (candidate.Option, candidate.Weight))
                                         .ToList()
                                 );
        var selected = highestPriorityCandidates.First(candidate => candidate.Option == selectedOption);

        foreach (var candidate in candidates.Where(candidate => !ReferenceEquals(candidate, selected)))
        {
            await RecordRuleExecutionAsync
            (
                sessionID,
                attr.ID,
                characterID,
                GetTransitionRuleID(candidate),
                stateEvent,
                true,
                false,
                cancellationToken
            );
        }

        return selected;
    }

    private static string GetTransitionRuleID(EnumTransitionConfig transition) =>
        string.IsNullOrWhiteSpace(transition.ID) ?
            transition.Option :
            transition.ID;

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
