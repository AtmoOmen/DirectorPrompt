using System.Text;
using System.Text.Json;
using DirectorPrompt.Agents.Pipeline;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Serilog;

namespace DirectorPrompt.Agents;

public sealed class Orchestrator
(
    IProjectRepository           projectRepository,
    ISessionRepository           sessionRepository,
    IEventRepository             eventRepository,
    ISceneRepository             sceneRepository,
    IDirectiveRepository         directiveRepository,
    IRoundChangeRepository       roundChangeRepository,
    IStateRepository             stateRepository,
    ISystemStateTransformer      systemStateTransformer,
    PhaseEvaluator               phaseEvaluator,
    CharacterTransitionEvaluator characterTransitionEvaluator,
    DirectiveProcessingStage     directiveProcessingStage,
    RetrievalStage               retrievalStage,
    GenerationStage              generationStage,
    AuditStage                   auditStage,
    PostProcessingStage          postProcessingStage
)
{
    public async Task<NarrationResult> ProcessBatchAsync
    (
        DirectiveBatch               batch,
        long                         sessionID,
        Action<string, string>?      onStreamingUpdate = null,
        Action<PipelineStageUpdate>? onStageUpdate     = null,
        CancellationToken            cancellationToken = default
    )
    {
        var project = await projectRepository.GetByIDAsync(batch.ProjectID, cancellationToken);

        if (project is null)
            throw new ArgumentException($"项目 {batch.ProjectID} 不存在");

        var session = await sessionRepository.GetByIDAsync(sessionID, cancellationToken);

        if (session is null)
            throw new ArgumentException($"对话 {sessionID} 不存在");

        var roundID          = await eventRepository.GetLatestRoundIDAsync(sessionID, cancellationToken) + 1;
        var activeScene      = await sceneRepository.GetActiveSceneAsync(sessionID, cancellationToken);
        var timelinePosition = activeScene?.TimelinePosition ?? 0;

        using (RoundContext.Enter(roundID))
        {
            Log.Information
            (
                "Orchestrator 开始处理批次: 项目={ProjectID} ({ProjectName}), 对话={SessionID}, 轮次={RoundID}, 场景={SceneID}, 指令数={DirectiveCount}",
                batch.ProjectID,
                project.Name,
                sessionID,
                roundID,
                activeScene?.ID,
                batch.Directives.Count
            );

            foreach (var d in batch.Directives)
                Log.Information("  指令 #{Order} [{Type}] {Content}", d.Order, d.Type, d.Content);

            var embeddingConfig = JsonSerializer.Deserialize<ModelConfig>(project.EmbeddingConfig) ?? new ModelConfig();

            var transitionResults = await EvaluateTransitionsAsync(batch.ProjectID, sessionID, roundID, cancellationToken);

            batch = InjectSystemDirectives(batch, transitionResults);

            var phaseResult = transitionResults
                              .Where(t => t.Source is PhaseEvaluator)
                              .Select(t => t.Result)
                              .FirstOrDefault();

            onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.DirectiveProcessing, PipelineStageStatus.Running));
            await directiveProcessingStage.ExecuteAsync
            (
                batch,
                sessionID,
                activeScene,
                embeddingConfig,
                cancellationToken
            );
            onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.DirectiveProcessing, PipelineStageStatus.Complete));

            activeScene = await sceneRepository.GetActiveSceneAsync(sessionID, cancellationToken);

            if (activeScene is null)
                throw new InvalidOperationException("场景创建失败: Scene Agent 未调用 create_scene 工具");

            timelinePosition = activeScene.TimelinePosition;

            var history = await BuildHistoryAsync(sessionID, roundID, cancellationToken);

            Log.Information("历史叙事注入: {HistoryCount} 轮", history.Count);

            var context = new PipelineContext
            {
                DirectiveBatch          = batch,
                RoundID                 = roundID,
                SessionID               = sessionID,
                CurrentSceneID          = activeScene.ID,
                CurrentTimelinePosition = timelinePosition,
                Project                 = project,
                EmbeddingConfig         = embeddingConfig,
                History                 = history,
                OnStreamingUpdate       = onStreamingUpdate,
                OnStageUpdate           = onStageUpdate,
                PhaseActivatedEntryIDs  = (phaseResult as PhaseEvaluationResult)?.ActivatedEntryIDs ?? []
            };

            var result = await RunPipelineAsync(context, transitionResults, cancellationToken);

            Log.Information
            (
                "Orchestrator 批次处理完成: 对话={SessionID}, 轮次={RoundID}, 审计通过={Passed}, 违规数={Violations}, 叙事长度={NarrativeLen}",
                sessionID,
                roundID,
                context.AuditPassed,
                context.Violations.Count,
                context.NarrativeOutput?.Length ?? 0
            );

            return result;
        }
    }

    public async Task DeleteRoundAsync(long sessionID, long roundID, CancellationToken cancellationToken = default)
    {
        Log.Information("删除轮次: 对话={SessionID}, 轮次={RoundID}", sessionID, roundID);

        await roundChangeRepository.RollbackRoundAsync(roundID, cancellationToken);
        await stateRepository.RollbackByRoundAsync(sessionID, roundID, cancellationToken);
        await roundChangeRepository.RemoveByRoundAsync(roundID, cancellationToken);
        await eventRepository.RemoveByRoundAsync(roundID, cancellationToken);
    }

    public async Task<NarrationResult> RewriteAsync
    (
        DirectiveBatch               batch,
        long                         sessionID,
        Action<string, string>?      onStreamingUpdate = null,
        Action<PipelineStageUpdate>? onStageUpdate     = null,
        CancellationToken            cancellationToken = default
    )
    {
        var latestRound = await eventRepository.GetLatestRoundIDAsync(sessionID, cancellationToken);

        if (latestRound > 0)
            await DeleteRoundAsync(sessionID, latestRound, cancellationToken);

        return await ProcessBatchAsync(batch, sessionID, onStreamingUpdate, onStageUpdate, cancellationToken);
    }

    public async Task<NarrationResult> CorrectAsync
    (
        long                         sessionID,
        long                         originalRoundID,
        string                       correctionGuidance,
        Action<string, string>?      onStreamingUpdate = null,
        Action<PipelineStageUpdate>? onStageUpdate     = null,
        CancellationToken            cancellationToken = default
    )
    {
        var session = await sessionRepository.GetByIDAsync(sessionID, cancellationToken);

        if (session is null)
            throw new ArgumentException($"对话 {sessionID} 不存在");

        var project = await projectRepository.GetByIDAsync(session.ProjectID, cancellationToken);

        if (project is null)
            throw new ArgumentException("项目不存在");

        var originalEvents = await eventRepository.GetByRoundAsync(originalRoundID, cancellationToken);
        var directorEvent  = originalEvents.FirstOrDefault(e => e.Type == EventType.DirectorInput);
        var narrativeEvent = originalEvents.FirstOrDefault(e => e.Type == EventType.NarrativeOutput);

        if (directorEvent is null || narrativeEvent is null)
            throw new ArgumentException("原始轮次事件不完整");

        var originalDirectives = ParseOriginalDirectives(directorEvent.Data);
        var batch              = new DirectiveBatch(project.ID, originalDirectives);

        var tempRoundID = await eventRepository.GetLatestRoundIDAsync(sessionID, cancellationToken) + 1;
        var activeScene = await sceneRepository.GetActiveSceneAsync(sessionID, cancellationToken);

        if (activeScene is null)
            throw new InvalidOperationException("修正需要已有活跃场景");

        var timelinePosition = activeScene.TimelinePosition;
        var history          = await BuildHistoryAsync(sessionID, tempRoundID, cancellationToken);
        var embeddingConfig  = JsonSerializer.Deserialize<ModelConfig>(project.EmbeddingConfig) ?? new ModelConfig();

        Log.Information
        (
            "Orchestrator 开始修正: 对话={SessionID}, 原轮次={OriginalRoundID}, 临时轮次={TempRoundID}, 修正指引={Guidance}",
            sessionID,
            originalRoundID,
            tempRoundID,
            correctionGuidance
        );

        using (RoundContext.Enter(tempRoundID))
        {
            var transitionResults = await EvaluateTransitionsAsync(project.ID, sessionID, tempRoundID, cancellationToken);

            batch = InjectSystemDirectives(batch, transitionResults);

            var phaseResult = transitionResults
                              .Where(t => t.Source is PhaseEvaluator)
                              .Select(t => t.Result)
                              .FirstOrDefault();

            var context = new PipelineContext
            {
                DirectiveBatch          = batch,
                RoundID                 = tempRoundID,
                SessionID               = sessionID,
                CurrentSceneID          = activeScene.ID,
                CurrentTimelinePosition = timelinePosition,
                Project                 = project,
                EmbeddingConfig         = embeddingConfig,
                History                 = history,
                OriginalNarrative       = narrativeEvent.Data,
                CorrectionGuidance      = correctionGuidance,
                OnStreamingUpdate       = onStreamingUpdate,
                OnStageUpdate           = onStageUpdate,
                PhaseActivatedEntryIDs  = (phaseResult as PhaseEvaluationResult)?.ActivatedEntryIDs ?? []
            };

            var result = await RunPipelineAsync(context, transitionResults, cancellationToken);

            Log.Information
            (
                "Orchestrator 修正完成: 对话={SessionID}, 临时轮次={TempRoundID}, 审计通过={Passed}",
                sessionID,
                tempRoundID,
                context.AuditPassed
            );

            return result;
        }
    }

    public async Task AcceptCorrectionAsync
    (
        long              sessionID,
        long              originalRoundID,
        long              tempRoundID,
        CancellationToken cancellationToken = default
    )
    {
        Log.Information
        (
            "接受修正: 对话={SessionID}, 原轮次={OriginalRoundID}, 临时轮次={TempRoundID}",
            sessionID,
            originalRoundID,
            tempRoundID
        );

        var capturedChanges      = await roundChangeRepository.CaptureRoundDataAsync(tempRoundID, cancellationToken);
        var tempStateChanges     = await stateRepository.CaptureStateChangesAsync(sessionID, tempRoundID,     cancellationToken);
        var originalStateChanges = await stateRepository.CaptureStateChangesAsync(sessionID, originalRoundID, cancellationToken);

        Log.Information
        (
            "修正数据捕获: 临时轮次变更数={ChangeCount}, 临时状态变更数={TempStateCount}, 原始状态变更数={OrigStateCount}",
            capturedChanges.Count,
            tempStateChanges.Count,
            originalStateChanges.Count
        );

        var tempEvents            = await eventRepository.GetByRoundAsync(tempRoundID, cancellationToken);
        var tempNarrativeEvent    = tempEvents.FirstOrDefault(e => e.Type == EventType.NarrativeOutput);
        var originalEvents        = await eventRepository.GetByRoundAsync(originalRoundID, cancellationToken);
        var originalDirectorEvent = originalEvents.FirstOrDefault(e => e.Type == EventType.DirectorInput);

        var projectID = originalDirectorEvent?.ProjectID ?? 0;

        Log.Information("修正: 删除临时轮次 {TempRoundID}", tempRoundID);
        await DeleteRoundAsync(sessionID, tempRoundID, cancellationToken);

        Log.Information("修正: 删除原始轮次 {OriginalRoundID}", originalRoundID);
        await DeleteRoundAsync(sessionID, originalRoundID, cancellationToken);

        Log.Information("修正: 重放临时轮次数据变更到原轮次, 变更数={ChangeCount}", capturedChanges.Count);
        await roundChangeRepository.ReplayChangesAsync(originalRoundID, capturedChanges, cancellationToken);

        Log.Information("修正: 重放状态变更, 临时状态变更数={TempStateCount}", tempStateChanges.Count);
        await stateRepository.ReplayStateChangesAsync
        (
            sessionID,
            originalRoundID,
            tempStateChanges,
            originalStateChanges,
            cancellationToken
        );

        if (originalDirectorEvent is not null)
        {
            await RecordEventAsync
            (
                projectID,
                sessionID,
                originalRoundID,
                EventType.DirectorInput,
                originalDirectorEvent.Data,
                cancellationToken
            );
        }

        if (tempNarrativeEvent is not null)
        {
            await RecordEventAsync
            (
                projectID,
                sessionID,
                originalRoundID,
                EventType.NarrativeOutput,
                tempNarrativeEvent.Data,
                cancellationToken
            );
        }

        Log.Information("修正已接受, 原轮次 {OriginalRoundID} 已替换", originalRoundID);
    }

    public async Task RejectCorrectionAsync
    (
        long              sessionID,
        long              tempRoundID,
        CancellationToken cancellationToken = default
    )
    {
        Log.Information("拒绝修正: 对话={SessionID}, 临时轮次={TempRoundID}", sessionID, tempRoundID);
        await DeleteRoundAsync(sessionID, tempRoundID, cancellationToken);
        Log.Information("修正已拒绝, 临时轮次 {TempRoundID} 已删除", tempRoundID);
    }

    private async Task<NarrationResult> RunPipelineAsync
    (
        PipelineContext                                                    context,
        IReadOnlyList<(ITransitionSource Source, TransitionResult Result)> transitionResults,
        CancellationToken                                                  cancellationToken
    )
    {
        context.OnStageUpdate?.Invoke
        (
            new PipelineStageUpdate(PipelineStageKind.Retrieval, PipelineStageStatus.Running)
        );

        await retrievalStage.ExecuteAsync(context, cancellationToken);

        context.OnStageUpdate?.Invoke
        (
            new PipelineStageUpdate
            (
                PipelineStageKind.Retrieval,
                PipelineStageStatus.Complete,
                $"知识长度={context.KnowledgeContext?.Length ?? 0}, 记忆长度={context.MemoryContext?.Length ?? 0}"
            )
        );

        context.OnStageUpdate?.Invoke
        (
            new PipelineStageUpdate(PipelineStageKind.Generation, PipelineStageStatus.Running)
        );

        await generationStage.ExecuteAsync(context, cancellationToken);

        context.OnStageUpdate?.Invoke
        (
            new PipelineStageUpdate
            (
                PipelineStageKind.Generation,
                PipelineStageStatus.Complete,
                $"叙事长度={context.NarrativeOutput?.Length ?? 0}"
            )
        );

        await RecordEventAsync
        (
            context.DirectiveBatch.ProjectID,
            context.SessionID,
            context.RoundID,
            EventType.DirectorInput,
            JsonSerializer.Serialize
            (
                context.DirectiveBatch.Directives.Select
                (d => new
                    {
                        type     = d.Type.ToString(),
                        content  = d.Content,
                        order    = d.Order,
                        isSystem = d.IsSystem
                    }
                )
            ),
            cancellationToken
        );

        await RecordEventAsync
        (
            context.DirectiveBatch.ProjectID,
            context.SessionID,
            context.RoundID,
            EventType.NarrativeOutput,
            context.NarrativeOutput ?? string.Empty,
            cancellationToken
        );

        await RecordTransitionAsync(context, transitionResults, cancellationToken);

        context.OnStageUpdate?.Invoke
        (
            new PipelineStageUpdate(PipelineStageKind.Audit, PipelineStageStatus.Running)
        );

        await auditStage.ExecuteAsync(context, cancellationToken);

        context.OnStageUpdate?.Invoke
        (
            new PipelineStageUpdate
            (
                PipelineStageKind.Audit,
                PipelineStageStatus.Complete,
                context.AuditPassed ?
                    "通过" :
                    $"违规数={context.Violations.Count}"
            )
        );

        if (context.AuditPassed)
        {
            context.OnStageUpdate?.Invoke
            (
                new PipelineStageUpdate(PipelineStageKind.PostProcessing, PipelineStageStatus.Running)
            );

            await postProcessingStage.ExecuteAsync(context, cancellationToken);

            context.OnStageUpdate?.Invoke
            (
                new PipelineStageUpdate(PipelineStageKind.PostProcessing, PipelineStageStatus.Complete)
            );
        }

        context.OnStageUpdate?.Invoke
        (
            new PipelineStageUpdate(PipelineStageKind.SystemState, PipelineStageStatus.Running)
        );

        await systemStateTransformer.ExecuteAsync
        (
            context.DirectiveBatch.ProjectID,
            context.SessionID,
            context.CurrentSceneID,
            context.RoundID,
            SystemTrigger.RoundEnd,
            cancellationToken
        );

        context.OnStageUpdate?.Invoke
        (
            new PipelineStageUpdate(PipelineStageKind.SystemState, PipelineStageStatus.Complete)
        );

        await directiveRepository.DecrementTTLAsync(context.SessionID, cancellationToken);

        return new NarrationResult
        (
            context.NarrativeOutput ?? string.Empty,
            context.ThinkingOutput  ?? string.Empty,
            context.RoundID,
            context.Violations,
            context.AuditPassed
        );
    }

    private async Task<IReadOnlyList<ChatHistoryEntry>> BuildHistoryAsync
    (
        long              sessionID,
        long              currentRoundID,
        CancellationToken cancellationToken
    )
    {
        var events = await eventRepository.GetBySessionAsync(sessionID, cancellationToken);

        var directorEvents = events
                             .Where(e => e.Type == EventType.DirectorInput)
                             .GroupBy(e => e.RoundID)
                             .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ID).First());

        var narrativeEvents = events
                              .Where(e => e.Type == EventType.NarrativeOutput)
                              .GroupBy(e => e.RoundID)
                              .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ID).First());

        var roundIDs = directorEvents.Keys
                                     .Concat(narrativeEvents.Keys)
                                     .Distinct()
                                     .Where(r => r < currentRoundID)
                                     .OrderBy(r => r)
                                     .ToList();

        var history = new List<ChatHistoryEntry>();

        foreach (var roundID in roundIDs)
        {
            var directorEntry  = directorEvents.GetValueOrDefault(roundID);
            var narrativeEntry = narrativeEvents.GetValueOrDefault(roundID);

            if (directorEntry is null || narrativeEntry is null)
                continue;

            var directorInput = ParseDirectorInput(directorEntry.Data);
            var narrativeText = narrativeEntry.Data;

            if (!string.IsNullOrWhiteSpace(narrativeText))
                history.Add(new ChatHistoryEntry(roundID, directorInput, narrativeText));
        }

        const int MAX_HISTORY_ROUNDS = 10;

        if (history.Count > MAX_HISTORY_ROUNDS)
            history = history.TakeLast(MAX_HISTORY_ROUNDS).ToList();

        return history;
    }

    private static string ParseDirectorInput(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            var sb = new StringBuilder();
            sb.AppendLine("## 导演指令");

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var type    = element.GetProperty("type").GetString();
                var content = element.GetProperty("content").GetString();
                var order   = element.GetProperty("order").GetInt32();
                sb.AppendLine($"{order}. [{type}] {content}");
            }

            return sb.ToString();
        }
        catch
        {
            return json;
        }
    }

    private static IReadOnlyList<DirectiveItem> ParseOriginalDirectives(string jsonData)
    {
        var result = new List<DirectiveItem>();

        using var doc = JsonDocument.Parse(jsonData);

        var order = 1;

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var typeStr = element.GetProperty("type").GetString()    ?? "Plot";
            var content = element.GetProperty("content").GetString() ?? string.Empty;

            var type = typeStr switch
            {
                "Tone"                => DirectiveType.Tone,
                "TemporaryConstraint" => DirectiveType.TemporaryConstraint,
                "SceneChange"         => DirectiveType.SceneChange,
                _                     => DirectiveType.Plot
            };

            var isSystem = element.TryGetProperty("isSystem", out var sysEl) && sysEl.GetBoolean();

            result.Add(new DirectiveItem(type, content, order++, IsSystem: isSystem));
        }

        return result;
    }

    private async Task RecordEventAsync
    (
        long              projectID,
        long              sessionID,
        long              roundID,
        EventType         type,
        string            data,
        CancellationToken cancellationToken
    )
    {
        Log.Debug
        (
            "记录事件: 类型={Type}, 对话={SessionID}, 轮次={RoundID}, 数据长度={DataLength}",
            type,
            sessionID,
            roundID,
            data.Length
        );

        var eventItem = new PlaythroughEvent
        {
            ProjectID = projectID,
            SessionID = sessionID,
            RoundID   = roundID,
            Type      = type,
            Data      = data,
            CreatedAt = DateTime.UtcNow
        };

        await eventRepository.AppendAsync(eventItem, cancellationToken);
    }

    private async Task<List<(ITransitionSource Source, TransitionResult Result)>> EvaluateTransitionsAsync
    (
        long              projectID,
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken
    )
    {
        var sources = new List<ITransitionSource> { phaseEvaluator, characterTransitionEvaluator };

        var results = new List<(ITransitionSource Source, TransitionResult Result)>();

        foreach (var source in sources)
        {
            var previousKeys = await GetPreviousTransitionKeysAsync(sessionID, roundID, source.EventType, cancellationToken);
            var result       = await source.EvaluateAsync(projectID, sessionID, previousKeys, cancellationToken);
            results.Add((source, result));
        }

        return results;
    }

    private async Task<IReadOnlyList<string>?> GetPreviousTransitionKeysAsync
    (
        long              sessionID,
        long              currentRoundID,
        EventType         eventType,
        CancellationToken cancellationToken
    )
    {
        var events = await eventRepository.GetBySessionAsync(sessionID, cancellationToken);

        var transitionEvent = events
                              .Where(e => e.Type == eventType && e.RoundID < currentRoundID)
                              .OrderByDescending(e => e.RoundID)
                              .FirstOrDefault();

        if (transitionEvent is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(transitionEvent.Data);

            if (doc.RootElement.TryGetProperty("activeKeys", out var keysEl) && keysEl.ValueKind == JsonValueKind.Array)
                return keysEl.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "解析上一轮 {EventType} 事件失败", eventType);
        }

        return null;
    }

    private static DirectiveBatch InjectSystemDirectives
    (
        DirectiveBatch                                                     batch,
        IReadOnlyList<(ITransitionSource Source, TransitionResult Result)> transitionResults
    )
    {
        var systemDirectives = new List<DirectiveItem>();

        var order = 1;

        foreach (var (_, result) in transitionResults)
        {
            foreach (var d in result.EnterDirectives)
            {
                systemDirectives.Add
                (
                    new DirectiveItem
                    (
                        d.Type,
                        d.Content,
                        order++,
                        d.TTL,
                        true
                    )
                );
            }

            foreach (var d in result.ExitDirectives)
            {
                systemDirectives.Add
                (
                    new DirectiveItem
                    (
                        d.Type,
                        d.Content,
                        order++,
                        d.TTL,
                        true
                    )
                );
            }
        }

        if (systemDirectives.Count == 0)
            return batch;

        var userDirectives = batch.Directives
                                  .Select(d => d with { Order = d.Order + systemDirectives.Count })
                                  .ToList();

        var allDirectives = systemDirectives.Concat(userDirectives).ToList();

        foreach (var (source, result) in transitionResults)
        {
            if (result.EnterDirectives.Count > 0 || result.ExitDirectives.Count > 0)
            {
                Log.Information
                (
                    "注入 {Source} 系统指令: 进入={EnterCount}, 退出={ExitCount}",
                    source.SourceName,
                    result.EnterDirectives.Count,
                    result.ExitDirectives.Count
                );
            }
        }

        Log.Information("系统指令注入完成: 总指令数={Total}", allDirectives.Count);

        return batch with { Directives = allDirectives };
    }

    private async Task RecordTransitionAsync
    (
        PipelineContext                                                    context,
        IReadOnlyList<(ITransitionSource Source, TransitionResult Result)> transitionResults,
        CancellationToken                                                  cancellationToken
    )
    {
        foreach (var (source, result) in transitionResults)
        {
            var data = JsonSerializer.Serialize(new { activeKeys = result.ActiveKeys });

            await RecordEventAsync
            (
                context.DirectiveBatch.ProjectID,
                context.SessionID,
                context.RoundID,
                source.EventType,
                data,
                cancellationToken
            );
        }
    }
}
