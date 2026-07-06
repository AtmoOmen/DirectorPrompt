using System.Text;
using System.Text.Json;
using DirectorPrompt.Agents.Pipeline;
using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents;

public sealed class Orchestrator
{
    private readonly IChatClientFactory       chatClientFactory;
    private readonly ISceneRepository         sceneRepository;
    private readonly IStateRepository         stateRepository;
    private readonly ICharacterRepository     characterRepository;
    private readonly IDirectiveRepository     directiveRepository;
    private readonly IEventRepository         eventRepository;
    private readonly IProjectRepository       projectRepository;
    private readonly ISessionRepository       sessionRepository;
    private readonly IKnowledgeRepository     knowledgeRepository;
    private readonly IMemoryRepository        memoryRepository;
    private readonly IEmbeddingService        embeddingService;
    private readonly ITimelineCalculator      timelineCalculator;
    private readonly IRoundChangeRepository   roundChangeRepository;
    private readonly OrchestratorConfig       config;

    private readonly SceneTools     sceneTools;
    private readonly KnowledgeTools knowledgeTools;
    private readonly StateTools     stateTools;
    private readonly MemoryTools    memoryTools;
    private readonly CharacterTools characterTools;
    private readonly AuditTools     auditTools;

    private readonly RetrievalStage      retrievalStage;
    private readonly GenerationStage     generationStage;
    private readonly AuditStage          auditStage;
    private readonly PostProcessingStage postProcessingStage;

    public Orchestrator
    (
        IChatClientFactory       chatClientFactory,
        ISceneRepository         sceneRepository,
        IStateRepository         stateRepository,
        ICharacterRepository     characterRepository,
        IDirectiveRepository     directiveRepository,
        IEventRepository         eventRepository,
        IProjectRepository       projectRepository,
        ISessionRepository       sessionRepository,
        IKnowledgeRepository     knowledgeRepository,
        IMemoryRepository        memoryRepository,
        IEmbeddingService        embeddingService,
        ITimelineCalculator      timelineCalculator,
        IRoundChangeRepository   roundChangeRepository,
        OrchestratorConfig       config
    )
    {
        this.chatClientFactory   = chatClientFactory;
        this.sceneRepository     = sceneRepository;
        this.stateRepository     = stateRepository;
        this.characterRepository = characterRepository;
        this.directiveRepository = directiveRepository;
        this.eventRepository     = eventRepository;
        this.projectRepository   = projectRepository;
        this.sessionRepository   = sessionRepository;
        this.knowledgeRepository = knowledgeRepository;
        this.memoryRepository    = memoryRepository;
        this.embeddingService    = embeddingService;
        this.timelineCalculator  = timelineCalculator;
        this.roundChangeRepository = roundChangeRepository;
        this.config              = config;

        sceneTools     = new SceneTools(sceneRepository, timelineCalculator);
        knowledgeTools = new KnowledgeTools(knowledgeRepository, embeddingService);
        stateTools     = new StateTools(stateRepository);
        memoryTools    = new MemoryTools(memoryRepository, embeddingService);
        characterTools = new CharacterTools(characterRepository, stateRepository);
        auditTools     = new AuditTools();

        retrievalStage = new RetrievalStage
        (
            chatClientFactory,
            sceneRepository,
            stateRepository,
            characterRepository,
            directiveRepository,
            knowledgeTools,
            memoryTools,
            config
        );

        generationStage = new GenerationStage
        (
            chatClientFactory,
            knowledgeTools,
            config
        );

        auditStage = new AuditStage
        (
            chatClientFactory,
            sceneTools,
            knowledgeTools,
            stateTools,
            memoryTools,
            characterTools,
            auditTools,
            config
        );

        postProcessingStage = new PostProcessingStage
        (
            chatClientFactory,
            memoryTools,
            stateTools,
            characterTools,
            config
        );
    }

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

        onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.DirectiveProcessing, PipelineStageStatus.Running));
        await ProcessDirectivesAsync(batch, sessionID, activeScene, cancellationToken);
        onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.DirectiveProcessing, PipelineStageStatus.Complete));

        var history = await BuildHistoryAsync(sessionID, roundID, cancellationToken);

        Log.Information("历史叙事注入: {HistoryCount} 轮", history.Count);

        var context = new PipelineContext
        {
            DirectiveBatch          = batch,
            RoundID                 = roundID,
            SessionID               = sessionID,
            CurrentSceneID          = activeScene?.ID,
            CurrentTimelinePosition = timelinePosition,
            Project                 = project,
            History                 = history,
            OnStreamingUpdate       = onStreamingUpdate,
            OnStageUpdate           = onStageUpdate
        };

        onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.Retrieval, PipelineStageStatus.Running));
        await retrievalStage.ExecuteAsync(context, cancellationToken);
        onStageUpdate?.Invoke
        (
            new PipelineStageUpdate
            (
                PipelineStageKind.Retrieval,
                PipelineStageStatus.Complete,
                $"知识长度={context.KnowledgeContext?.Length ?? 0}, 记忆长度={context.MemoryContext?.Length ?? 0}"
            )
        );

        onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.Generation, PipelineStageStatus.Running));
        await generationStage.ExecuteAsync(context, cancellationToken);
        onStageUpdate?.Invoke
        (
            new PipelineStageUpdate
            (
                PipelineStageKind.Generation,
                PipelineStageStatus.Complete,
                $"叙事长度={context.NarrativeOutput?.Length ?? 0}"
            )
        );

        Log.Information
        (
            "Narrator 输出 (轮次={RoundID}):\n{Narrative}",
            roundID,
            context.NarrativeOutput ?? "(空)"
        );

        if (!string.IsNullOrWhiteSpace(context.ThinkingOutput))
            Log.Debug("Narrator 思考 (轮次={RoundID}):\n{Thinking}", roundID, context.ThinkingOutput);

        await RecordEventAsync
        (
            batch.ProjectID,
            sessionID,
            roundID,
            EventType.DirectorInput,
            JsonSerializer.Serialize
            (
                batch.Directives.Select
                (d => new
                    {
                        type    = d.Type.ToString(),
                        content = d.Content,
                        order   = d.Order
                    }
                )
            ),
            cancellationToken
        );

        await RecordEventAsync
        (
            batch.ProjectID,
            sessionID,
            roundID,
            EventType.NarrativeOutput,
            context.NarrativeOutput ?? string.Empty,
            cancellationToken
        );

        onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.Audit, PipelineStageStatus.Running));
        await RunAuditLoopAsync(context, cancellationToken);
        onStageUpdate?.Invoke
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
            onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.PostProcessing, PipelineStageStatus.Running));
            await postProcessingStage.ExecuteAsync(context, cancellationToken);
            onStageUpdate?.Invoke(new PipelineStageUpdate(PipelineStageKind.PostProcessing, PipelineStageStatus.Complete));
        }

        await directiveRepository.DecrementTTLAsync(sessionID, cancellationToken);

        Log.Information
        (
            "Orchestrator 批次处理完成: 对话={SessionID}, 轮次={RoundID}, 审计通过={Passed}, 违规数={Violations}, 叙事长度={NarrativeLen}",
            sessionID,
            roundID,
            context.AuditPassed,
            context.Violations.Count,
            context.NarrativeOutput?.Length ?? 0
        );

        return new NarrationResult
        (
            context.NarrativeOutput ?? string.Empty,
            context.ThinkingOutput  ?? string.Empty,
            roundID,
            context.Violations,
            context.AuditPassed
        );
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

    private async Task ProcessDirectivesAsync
    (
        DirectiveBatch    batch,
        long              sessionID,
        Scene?            activeScene,
        CancellationToken cancellationToken
    )
    {
        foreach (var directive in batch.Directives)
        {
            if (directive.Type is DirectiveType.Tone or DirectiveType.TemporaryConstraint)
            {
                var ttl = directive.Type == DirectiveType.Tone ?
                              5 :
                              (int?)null;

                Log.Information
                (
                    "添加生效指令: 类型={Type}, 内容={Content}, TTL={TTL}",
                    directive.Type,
                    directive.Content,
                    ttl?.ToString() ?? "永久"
                );

                await directiveRepository.AddAsync
                (
                    new ActiveDirective
                    {
                        ProjectID = batch.ProjectID,
                        SessionID = sessionID,
                        Type      = directive.Type,
                        Content   = directive.Content,
                        TTL       = ttl,
                        CreatedAt = DateTime.UtcNow
                    },
                    cancellationToken
                );
            }

            if (directive.Type == DirectiveType.SceneChange)
                await CreateSceneViaAgentAsync(batch.ProjectID, sessionID, directive.Content, activeScene, cancellationToken);
        }
    }

    private async Task CreateSceneViaAgentAsync
    (
        long              projectID,
        long              sessionID,
        string            description,
        Scene?            currentScene,
        CancellationToken cancellationToken
    )
    {
        var sceneAgent = config.Agents.FirstOrDefault(a => a.Role == AgentRole.Scene);

        if (sceneAgent is null || !sceneAgent.Enabled)
        {
            Log.Debug("Scene Agent 未启用, 跳过场景创建");
            return;
        }

        Log.Information
        (
            "场景创建: 模型={Model}, 描述={Description}",
            sceneAgent.ModelConfig.ModelName,
            description
        );

        var toolContext = new ToolExecutionContext
        (
            projectID,
            sessionID,
            currentScene?.ID,
            currentScene?.TimelinePosition ?? 0,
            0
        );

        var client = chatClientFactory.Create(sceneAgent.ModelConfig);
        var tools  = sceneTools.Create(toolContext);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SceneAgentPrompt.System),
            new(ChatRole.User, description)
        };

        var options = new ChatOptions
        {
            Temperature = sceneAgent.Temperature,
            ModelId     = sceneAgent.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        await client.GetResponseAsync(messages, options, cancellationToken);

        Log.Information("场景创建完成");
    }

    private async Task RunAuditLoopAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        var maxRetries = config.AuditConfig.MaxRetries;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            Log.Information
            (
                "审计循环: 尝试={Attempt}/{MaxRetries}",
                attempt,
                maxRetries
            );

            await auditStage.ExecuteAsync(context, cancellationToken);
            context.AuditRetryCount = attempt;

            if (context.AuditPassed)
            {
                Log.Information("审计通过, 退出循环");
                return;
            }

            if (attempt < maxRetries && config.AuditConfig.Mode == AuditMode.Blocking)
            {
                Log.Warning
                (
                    "审计未通过, 准备重试: 违规数={ViolationCount}",
                    context.Violations.Count
                );

                await generationStage.RetryWithFeedbackAsync(context, context.Violations, cancellationToken);
            }
            else
            {
                Log.Warning("达到最大重试次数或非阻塞模式, 强制通过");
                context.AuditPassed = true;
                return;
            }
        }
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

        const int maxHistoryRounds = 10;

        if (history.Count > maxHistoryRounds)
            history = history.TakeLast(maxHistoryRounds).ToList();

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

        if (type == EventType.NarrativeOutput && data.Length > 0)
        {
            Log.Debug
            (
                "  事件数据 (NarrativeOutput): {Data}",
                data.Length > 500 ?
                    data[..500] + "..." :
                    data
            );
        }
        else if (type == EventType.DirectorInput)
            Log.Debug("  事件数据 (DirectorInput): {Data}", data);

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
}
