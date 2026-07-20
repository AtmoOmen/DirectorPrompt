using System.Diagnostics;
using System.Text.Json;
using DirectorPrompt.Agents.Config;
using DirectorPrompt.Agents.Pipeline;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Serilog;
using Serilog.Context;

namespace DirectorPrompt.Agents;

public sealed class Orchestrator
(
    IProjectRepository       projectRepository,
    ISessionRepository       sessionRepository,
    IEventRepository         eventRepository,
    ISceneRepository         sceneRepository,
    IDirectiveRepository     directiveRepository,
    IRoundChangeRepository   roundChangeRepository,
    IStateRepository         stateRepository,
    ISystemStateTransformer  systemStateTransformer,
    PhaseEvaluator           phaseEvaluator,
    DirectiveProcessingStage directiveProcessingStage,
    RetrievalStage           retrievalStage,
    GenerationStage          generationStage,
    PostProcessingStage      postProcessingStage,
    SceneSummaryStage        sceneSummaryStage,
    HistoryBuilder           historyBuilder,
    AgentConfigResolver      agentConfigResolver,
    OrchestratorConfig       orchestratorConfig,
    UserSettings             userSettings
)
{
    public async Task<NarrationResult> ProcessBatchAsync
    (
        DirectiveBatch                batch,
        long                          sessionID,
        Action<string, string, bool>? onStreamingUpdate = null,
        Action<PipelineStageUpdate>?  onStageUpdate     = null,
        CancellationToken             cancellationToken = default
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
        var oldSceneID       = activeScene?.ID;
        var timelinePosition = activeScene?.TimelinePosition ?? 0;
        var batchStopwatch   = Stopwatch.StartNew();
        var pipelineID       = Guid.NewGuid().ToString("N");

        using var pipelineContext = LogContext.PushProperty("PipelineID", pipelineID);
        using var projectContext  = LogContext.PushProperty("ProjectID", batch.ProjectID);
        using var sessionContext  = LogContext.PushProperty("SessionID", sessionID);
        using var roundContext    = LogContext.PushProperty("RoundID", roundID);

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
            Log.Debug("指令元数据: 顺序={Order}, 类型={Type}, 长度={Length}", d.Order, d.Type, d.Content.Length);

        var embeddingConfig = ResolveEmbeddingConfig();

        var transitionResults = await EvaluateTransitionsAsync(batch.ProjectID, sessionID, roundID, cancellationToken);

        batch = InjectSystemDirectives(batch, transitionResults);

        Log.Information
        (
            "系统指令已合并: 轮次={RoundID}, 合并后指令数={DirectiveCount}, 转换来源数={TransitionSourceCount}",
            roundID,
            batch.Directives.Count,
            transitionResults.Count
        );

        var phaseResult = transitionResults
                          .Where(t => t.Source is PhaseEvaluator)
                          .Select(t => t.Result)
                          .FirstOrDefault();

        onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.DirectiveProcessing, PipelineStageStatus.Running));

        try
        {
            await directiveProcessingStage.ExecuteAsync
            (
                batch,
                sessionID,
                roundID,
                activeScene,
                embeddingConfig,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.DirectiveProcessing, PipelineStageStatus.Failed));
            Log.Information("指令处理阶段已取消: 耗时={ElapsedMilliseconds}ms", batchStopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.DirectiveProcessing, PipelineStageStatus.Failed));
            Log.Error(exception, "指令处理阶段失败: 耗时={ElapsedMilliseconds}ms", batchStopwatch.ElapsedMilliseconds);
            throw;
        }

        onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.DirectiveProcessing, PipelineStageStatus.Complete));

        activeScene = await sceneRepository.GetActiveSceneAsync(sessionID, cancellationToken);

        if (activeScene is null)
            throw new InvalidOperationException("场景创建失败: Scene Agent 未调用 create_scene 工具");

        if (oldSceneID is not null && oldSceneID != activeScene.ID)
        {
            Log.Information("场景切换: 旧场景={OldSceneID}, 新场景={NewSceneID}", oldSceneID, activeScene.ID);
            await sceneSummaryStage.ExecuteAsync(sessionID, roundID, oldSceneID.Value, cancellationToken);
        }

        var previousScene        = await sceneRepository.GetLastCompletedSceneAsync(sessionID, activeScene.ID, cancellationToken);
        var previousSceneSummary = previousScene?.Summary;

        activeScene = await sceneSummaryStage.UpdateProgressSummaryAsync
                      (
                          sessionID,
                          activeScene,
                          roundID,
                          cancellationToken
                      );

        timelinePosition = activeScene.TimelinePosition;

        var history = await historyBuilder.BuildAsync(sessionID, activeScene.ID, roundID, cancellationToken);

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
            KnowledgeConfig         = orchestratorConfig.KnowledgeConfig,
            MemoryConfig            = orchestratorConfig.MemoryConfig,
            History                 = history,
            PreviousSceneSummary    = previousSceneSummary,
            OnStreamingUpdate       = onStreamingUpdate,
            OnStageUpdate           = onStageUpdate,
            PhaseActivatedEntryIDs  = (phaseResult as PhaseEvaluationResult)?.ActivatedEntryIDs ?? []
        };

        var result = await RunPipelineAsync(context, transitionResults, cancellationToken);

        Log.Information
        (
            "Orchestrator 批次处理完成: 对话={SessionID}, 轮次={RoundID}, 叙事长度={NarrativeLen}, 思考长度={ThinkingLen}, 耗时={ElapsedMilliseconds}ms",
            sessionID,
            roundID,
            context.NarrativeOutput?.Length ?? 0,
            context.ThinkingOutput?.Length ?? 0,
            batchStopwatch.ElapsedMilliseconds
        );

        return result;
    }

    public async Task DeleteRoundAsync(long sessionID, long roundID, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        Log.Information("删除轮次: 对话={SessionID}, 轮次={RoundID}", sessionID, roundID);

        await roundChangeRepository.RollbackRoundAsync(sessionID, roundID, cancellationToken);
        await stateRepository.RollbackByRoundAsync(sessionID, roundID, cancellationToken);
        await roundChangeRepository.RemoveByRoundAsync(sessionID, roundID, cancellationToken);
        await eventRepository.RemoveByRoundAsync(sessionID, roundID, cancellationToken);

        Log.Information
        (
            "轮次删除完成: 对话={SessionID}, 轮次={RoundID}, 耗时={ElapsedMilliseconds}ms",
            sessionID,
            roundID,
            stopwatch.ElapsedMilliseconds
        );
    }

    public async Task<RollbackResult?> RollbackLastRoundAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        var latestRound = await eventRepository.GetLatestRoundIDAsync(sessionID, cancellationToken);

        if (latestRound <= 0)
        {
            Log.Information("回退请求已忽略: 对话={SessionID} 没有可回退轮次", sessionID);
            return null;
        }

        var events        = await eventRepository.GetByRoundAsync(sessionID, latestRound, cancellationToken);
        var directorEvent = events.FirstOrDefault(e => e.Type == EventType.DirectorInput);

        Log.Information("用户回退轮次: 对话={SessionID}, 轮次={RoundID}", sessionID, latestRound);

        await DeleteRoundAsync(sessionID, latestRound, cancellationToken);

        var directives = directorEvent is not null ?
                             EventDataSerializer.ParseDirectives(directorEvent.Data) :
                             [];

        Log.Information
        (
            "轮次回退完成: 对话={SessionID}, 轮次={RoundID}, 已恢复指令数={DirectiveCount}",
            sessionID,
            latestRound,
            directives.Count
        );

        return new RollbackResult(latestRound, directives);
    }

    public async Task TryDeleteRoundAsync(long sessionID, long roundID, CancellationToken cancellationToken = default)
    {
        if (roundID <= 0)
        {
            Log.Debug("忽略无效轮次删除请求: 对话={SessionID}, 轮次={RoundID}", sessionID, roundID);
            return;
        }

        var events = await eventRepository.GetByRoundAsync(sessionID, roundID, cancellationToken);

        if (events.Count == 0)
        {
            Log.Debug("轮次无需删除: 对话={SessionID}, 轮次={RoundID}", sessionID, roundID);
            return;
        }

        await DeleteRoundAsync(sessionID, roundID, cancellationToken);
    }

    private ResolvedEmbeddingConfig ResolveEmbeddingConfig()
    {
        var resolved = agentConfigResolver.ResolveEmbedding(userSettings.EmbeddingConfig);

        if (resolved is null)
            throw new InvalidOperationException("向量模型配置无效: 未找到对应的提供商");

        Log.Information
        (
            "已解析向量模型配置: 提供商={Provider}, 模型={Model}",
            resolved.Provider,
            resolved.ModelName
        );

        return resolved;
    }

    private static async Task RunStageAsync
    (
        PipelineContext   context,
        PipelineStageKind kind,
        Func<Task>        action,
        Func<string?>?    detailFactory = null
    )
    {
        var stopwatch = Stopwatch.StartNew();

        Log.Information
        (
            "流水线阶段开始: 阶段={Stage}, 对话={SessionID}, 轮次={RoundID}",
            kind,
            context.SessionID,
            context.RoundID
        );

        context.OnStageUpdate?.Invoke(new PipelineStageUpdate(kind, PipelineStageStatus.Running));

        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            context.OnStageUpdate?.Invoke(new PipelineStageUpdate(kind, PipelineStageStatus.Failed));
            Log.Information
            (
                "流水线阶段已取消: 阶段={Stage}, 对话={SessionID}, 轮次={RoundID}, 耗时={ElapsedMilliseconds}ms",
                kind,
                context.SessionID,
                context.RoundID,
                stopwatch.ElapsedMilliseconds
            );
            throw;
        }
        catch (Exception exception)
        {
            context.OnStageUpdate?.Invoke(new PipelineStageUpdate(kind, PipelineStageStatus.Failed));
            Log.Error
            (
                exception,
                "流水线阶段失败: 阶段={Stage}, 对话={SessionID}, 轮次={RoundID}, 耗时={ElapsedMilliseconds}ms",
                kind,
                context.SessionID,
                context.RoundID,
                stopwatch.ElapsedMilliseconds
            );
            throw;
        }

        var detail = detailFactory?.Invoke();

        context.OnStageUpdate?.Invoke
        (
            new PipelineStageUpdate(kind, PipelineStageStatus.Complete, detail)
        );

        Log.Information
        (
            "流水线阶段完成: 阶段={Stage}, 对话={SessionID}, 轮次={RoundID}, 详情={Detail}, 耗时={ElapsedMilliseconds}ms",
            kind,
            context.SessionID,
            context.RoundID,
            detail,
            stopwatch.ElapsedMilliseconds
        );
    }

    private async Task<NarrationResult> RunPipelineAsync
    (
        PipelineContext                                                    context,
        IReadOnlyList<(ITransitionSource Source, TransitionResult Result)> transitionResults,
        CancellationToken                                                  cancellationToken
    )
    {
        await RunStageAsync
        (
            context,
            PipelineStageKind.Retrieval,
            () => retrievalStage.ExecuteAsync(context, cancellationToken),
            () => $"知识长度={context.KnowledgeContext?.Length ?? 0}, 记忆长度={context.MemoryContext?.Length ?? 0}"
        );

        await RunStageAsync
        (
            context,
            PipelineStageKind.Generation,
            () => generationStage.ExecuteAsync(context, cancellationToken),
            () => $"叙事长度={context.NarrativeOutput?.Length ?? 0}"
        );

        var now = DateTime.UtcNow;

        var events = new List<PlaythroughEvent>
        {
            new()
            {
                ProjectID = context.DirectiveBatch.ProjectID,
                SessionID = context.SessionID,
                RoundID   = context.RoundID,
                SceneID   = context.CurrentSceneID,
                Type      = EventType.DirectorInput,
                Data = JsonSerializer.Serialize
                (
                    context.DirectiveBatch.Directives.Select
                    (d => new DirectiveEventData
                        {
                            Type     = d.Type.ToString(),
                            Content  = d.Content,
                            Order    = d.Order,
                            TTL      = d.TTL,
                            IsSystem = d.IsSystem
                        }
                    ),
                    JsonOptions.Compact
                ),
                CreatedAt = now
            },
            new()
            {
                ProjectID = context.DirectiveBatch.ProjectID,
                SessionID = context.SessionID,
                RoundID   = context.RoundID,
                SceneID   = context.CurrentSceneID,
                Type      = EventType.NarrativeOutput,
                Data      = context.NarrativeOutput ?? string.Empty,
                CreatedAt = now
            }
        };

        foreach (var (source, result) in transitionResults)
        {
            events.Add
            (
                new PlaythroughEvent
                {
                    ProjectID = context.DirectiveBatch.ProjectID,
                    SessionID = context.SessionID,
                    RoundID   = context.RoundID,
                    SceneID   = context.CurrentSceneID,
                    Type      = source.EventType,
                    Data      = JsonSerializer.Serialize(new TransitionEventData { ActiveKeys = result.ActiveKeys }, JsonOptions.Compact),
                    CreatedAt = now
                }
            );
        }

        await eventRepository.AppendBatchAsync(events, cancellationToken);

        Log.Information
        (
            "对话事件已持久化: 对话={SessionID}, 轮次={RoundID}, 事件数={EventCount}",
            context.SessionID,
            context.RoundID,
            events.Count
        );

        await RunStageAsync
        (
            context,
            PipelineStageKind.PostProcessing,
            () => postProcessingStage.ExecuteAsync(context, cancellationToken)
        );

        await RunStageAsync
        (
            context,
            PipelineStageKind.SystemState,
            () => systemStateTransformer.ExecuteAsync
            (
                context.DirectiveBatch.ProjectID,
                context.SessionID,
                context.CurrentSceneID,
                context.RoundID,
                SystemTrigger.RoundEnd,
                cancellationToken
            )
        );

        await directiveRepository.DecrementTTLAsync(context.SessionID, context.RoundID, cancellationToken);

        Log.Information
        (
            "有效指令 TTL 已更新: 对话={SessionID}, 轮次={RoundID}",
            context.SessionID,
            context.RoundID
        );

        return new NarrationResult
        (
            context.NarrativeOutput ?? string.Empty,
            context.ThinkingOutput  ?? string.Empty,
            context.RoundID
        );
    }

    private async Task<List<(ITransitionSource Source, TransitionResult Result)>> EvaluateTransitionsAsync
    (
        long              projectID,
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken
    )
    {
        var sources = new List<ITransitionSource> { phaseEvaluator };

        var results = new List<(ITransitionSource Source, TransitionResult Result)>();

        foreach (var source in sources)
        {
            var previousKeys = await GetPreviousTransitionKeysAsync(sessionID, roundID, source.EventType, cancellationToken);
            var stopwatch    = Stopwatch.StartNew();

            Log.Debug
            (
                "开始评估状态转换: 来源={Source}, 事件类型={EventType}, 上一轮激活键数={PreviousKeyCount}",
                source.GetType().Name,
                source.EventType,
                previousKeys?.Count ?? 0
            );

            var result       = await source.EvaluateAsync(projectID, sessionID, previousKeys, cancellationToken);
            results.Add((source, result));

            Log.Information
            (
                "状态转换评估完成: 来源={Source}, 事件类型={EventType}, 激活键数={ActiveKeyCount}, 进入指令数={EnterDirectiveCount}, 退出指令数={ExitDirectiveCount}, 耗时={ElapsedMilliseconds}ms",
                source.GetType().Name,
                source.EventType,
                result.ActiveKeys.Count,
                result.EnterDirectives.Count,
                result.ExitDirectives.Count,
                stopwatch.ElapsedMilliseconds
            );
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
        var transitionEvent = await eventRepository.GetLatestByTypeBeforeRoundAsync
                              (
                                  sessionID,
                                  eventType,
                                  currentRoundID,
                                  cancellationToken
                              );

        if (transitionEvent is null)
            return null;

        try
        {
            var data = JsonSerializer.Deserialize<TransitionEventData>(transitionEvent.Data, JsonOptions.Compact);

            return data?.ActiveKeys;
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

        Log.Information("已注入系统指令: 数量={SystemDirectiveCount}", systemDirectives.Count);

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

    private sealed class TransitionEventData
    {
        public IReadOnlyList<string>? ActiveKeys { get; set; }
    }
}
