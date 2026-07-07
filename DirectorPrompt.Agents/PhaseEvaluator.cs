using System.Globalization;
using System.Text.Json;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Serilog;

namespace DirectorPrompt.Agents;

public sealed record PhaseEvaluationResult : TransitionResult
{
    public required IReadOnlyList<long> ActivatedEntryIDs { get; init; }
}

public sealed class PhaseEvaluator
(
    IStateRepository     stateRepository,
    IKnowledgeRepository knowledgeRepository,
    IConditionEngine     conditionEngine
) : ITransitionSource
{
    public string SourceName => "Phase";

    public EventType EventType => EventType.PhaseTransition;

    async Task<TransitionResult> ITransitionSource.EvaluateAsync
    (
        long                   projectID,
        long                   sessionID,
        IReadOnlyList<string>? previousKeys,
        CancellationToken      cancellationToken
    ) =>
        await EvaluateAsync(projectID, sessionID, previousKeys, cancellationToken);

    public async Task<PhaseEvaluationResult> EvaluateAsync
    (
        long              projectID,
        long              sessionID,
        IReadOnlyList<string>? previousActivePhaseKeys,
        CancellationToken cancellationToken = default
    )
    {
        var attributes = await stateRepository.GetAttributesAsync(projectID, StateScope.Global, cancellationToken);

        var entryIDs = new HashSet<long>();
        var groupIDs = new HashSet<long>();
        var allPhases = new List<(Phase Phase, long AttributeID)>();
        var activePhases = new List<(Phase Phase, long AttributeID)>();

        foreach (var attr in attributes)
        {
            var phases = ParsePhases(attr.Config);

            if (phases.Count == 0)
                continue;

            var value = await stateRepository.GetStateValueAsync(attr.ID, sessionID, cancellationToken);
            var currentValue = value?.Value ?? string.Empty;

            foreach (var phase in phases)
            {
                allPhases.Add((phase, attr.ID));

                if (string.IsNullOrEmpty(currentValue))
                    continue;

                if (!EvaluatePhaseExpression(phase.Expression, currentValue))
                    continue;

                Log.Information
                (
                    "Phase 激活: {PhaseName} (属性={AttrName}, 值={Value})",
                    phase.Name,
                    attr.Name,
                    currentValue
                );

                activePhases.Add((phase, attr.ID));

                foreach (var id in phase.KnowledgeIDs)
                    entryIDs.Add(id);

                foreach (var id in phase.KnowledgeGroupIDs)
                    groupIDs.Add(id);
            }
        }

        foreach (var groupID in groupIDs)
        {
            var groupEntries = await knowledgeRepository.GetByGroupAsync(groupID, cancellationToken);

            foreach (var entry in groupEntries)
                entryIDs.Add(entry.ID);
        }

        var activePhaseKeys = activePhases
                              .Select(p => $"{p.AttributeID}:{p.Phase.Name}")
                              .ToList();

        var previousSet = previousActivePhaseKeys is not null
                              ? new HashSet<string>(previousActivePhaseKeys)
                              : [];

        var currentSet = new HashSet<string>(activePhaseKeys);

        var enterDirectives = activePhases
                              .Where(p => !previousSet.Contains($"{p.AttributeID}:{p.Phase.Name}"))
                              .SelectMany(p => p.Phase.EnterDirectives)
                              .ToList();

        var exitDirectives = previousActivePhaseKeys is not null
                                  ? allPhases
                                    .Where(p => previousSet.Contains($"{p.AttributeID}:{p.Phase.Name}")
                                                && !currentSet.Contains($"{p.AttributeID}:{p.Phase.Name}"))
                                    .SelectMany(p => p.Phase.ExitDirectives)
                                    .ToList()
                                  : [];

        if (enterDirectives.Count > 0 || exitDirectives.Count > 0)
        {
            Log.Information
            (
                "Phase 转换: 进入指令数={EnterCount}, 退出指令数={ExitCount}, 激活知识条目数={KnowledgeCount}",
                enterDirectives.Count,
                exitDirectives.Count,
                entryIDs.Count
            );
        }
        else if (entryIDs.Count > 0)
        {
            Log.Information("Phase 评估完成: 激活知识条目数={Count}", entryIDs.Count);
        }

        return new PhaseEvaluationResult
        {
            ActivatedEntryIDs = entryIDs.ToList(),
            EnterDirectives   = enterDirectives,
            ExitDirectives    = exitDirectives,
            ActiveKeys        = activePhaseKeys
        };
    }

    internal static List<Phase> ParsePhases(string config)
    {
        var result = new List<Phase>();

        try
        {
            using var doc = JsonDocument.Parse(config);

            if (!doc.RootElement.TryGetProperty("phases", out var phasesEl) || phasesEl.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var ph in phasesEl.EnumerateArray())
            {
                var name = ph.TryGetProperty("name", out var n) && n.ValueKind != JsonValueKind.Null
                               ? n.GetString() ?? string.Empty
                               : string.Empty;

                var expression = ph.TryGetProperty("expression", out var e) && e.ValueKind != JsonValueKind.Null
                                     ? e.GetString() ?? string.Empty
                                     : string.Empty;

                var knowledgeIds = ph.TryGetProperty("knowledgeIds", out var kid) && kid.ValueKind == JsonValueKind.Array
                                       ? kid.EnumerateArray().Select(v => v.GetInt64()).ToList()
                                       : new List<long>();

                var knowledgeGroupIds = ph.TryGetProperty("knowledgeGroupIds", out var gid) && gid.ValueKind == JsonValueKind.Array
                                            ? gid.EnumerateArray().Select(v => v.GetInt64()).ToList()
                                            : new List<long>();

                var enterDirectives = ParseDirectiveConfigs(ph, "enterDirectives");
                var exitDirectives = ParseDirectiveConfigs(ph, "exitDirectives");

                result.Add
                (
                    new Phase
                    {
                        Name              = name,
                        Expression        = expression,
                        KnowledgeIDs      = knowledgeIds,
                        KnowledgeGroupIDs = knowledgeGroupIds,
                        EnterDirectives   = enterDirectives,
                        ExitDirectives    = exitDirectives
                    }
                );
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "解析 Phase 配置失败");
        }

        return result;
    }

    private static List<DirectiveConfig> ParseDirectiveConfigs(JsonElement phaseEl, string propertyName)
    {
        var result = new List<DirectiveConfig>();

        if (!phaseEl.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in arr.EnumerateArray())
        {
            var typeStr = item.TryGetProperty("type", out var t) && t.ValueKind != JsonValueKind.Null
                              ? t.GetString() ?? "Plot"
                              : "Plot";

            var type = typeStr switch
            {
                "Tone"                => DirectiveType.Tone,
                "TemporaryConstraint" => DirectiveType.TemporaryConstraint,
                "SceneChange"         => DirectiveType.SceneChange,
                _                     => DirectiveType.Plot
            };

            var content = item.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null
                              ? c.GetString() ?? string.Empty
                              : string.Empty;

            var ttl = item.TryGetProperty("ttl", out var ttlEl) && ttlEl.ValueKind == JsonValueKind.Number
                          ? ttlEl.GetInt32()
                          : (int?)null;

            result.Add
            (
                new DirectiveConfig
                {
                    Type    = type,
                    Content = content,
                    TTL     = ttl
                }
            );
        }

        return result;
    }

    private bool EvaluatePhaseExpression(string expression, string currentValue)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var isNumeric = float.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        var valReplacement = isNumeric ? currentValue : $"\"{currentValue}\"";

        var expr = expression.Replace("{val}", valReplacement);
        expr = expr.Replace(" AND ", " && ").Replace(" OR ", " || ");

        try
        {
            return conditionEngine.Evaluate(expr, new ConditionContext(new Dictionary<string, string>()));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Phase 表达式求值失败: {Expression}", expression);
            return false;
        }
    }
}
