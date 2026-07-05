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
    private readonly IStateSnapshotRepository snapshotRepository;
    private readonly IProjectRepository       projectRepository;
    private readonly IKnowledgeRepository     knowledgeRepository;
    private readonly IMemoryRepository        memoryRepository;
    private readonly IEmbeddingService        embeddingService;
    private readonly ITimelineCalculator      timelineCalculator;
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
        IStateSnapshotRepository snapshotRepository,
        IProjectRepository       projectRepository,
        IKnowledgeRepository     knowledgeRepository,
        IMemoryRepository        memoryRepository,
        IEmbeddingService        embeddingService,
        ITimelineCalculator      timelineCalculator,
        OrchestratorConfig       config
    )
    {
        this.chatClientFactory   = chatClientFactory;
        this.sceneRepository     = sceneRepository;
        this.stateRepository     = stateRepository;
        this.characterRepository = characterRepository;
        this.directiveRepository = directiveRepository;
        this.eventRepository     = eventRepository;
        this.snapshotRepository  = snapshotRepository;
        this.projectRepository   = projectRepository;
        this.knowledgeRepository = knowledgeRepository;
        this.memoryRepository    = memoryRepository;
        this.embeddingService    = embeddingService;
        this.timelineCalculator  = timelineCalculator;
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
        DirectiveBatch             batch,
        Action<string, string>?    onStreamingUpdate = null,
        CancellationToken          cancellationToken = default
    )
    {
        var project = await projectRepository.GetByIDAsync(batch.ProjectID, cancellationToken);

        if (project is null)
            throw new ArgumentException($"项目 {batch.ProjectID} 不存在");

        var roundID          = await eventRepository.GetLatestRoundIDAsync(batch.ProjectID, cancellationToken) + 1;
        var activeScene      = await sceneRepository.GetActiveSceneAsync(batch.ProjectID, cancellationToken);
        var timelinePosition = activeScene?.TimelinePosition ?? 0;

        Log.Information
        (
            "Orchestrator 开始处理批次: 项目={ProjectID}, 轮次={RoundID}, 场景={SceneID}, 指令数={DirectiveCount}",
            batch.ProjectID,
            roundID,
            activeScene?.ID,
            batch.Directives.Count
        );

        await ProcessDirectivesAsync(batch, roundID, activeScene, cancellationToken);

        var context = new PipelineContext
        {
            DirectiveBatch          = batch,
            RoundID                 = roundID,
            CurrentSceneID          = activeScene?.ID,
            CurrentTimelinePosition = timelinePosition,
            OnStreamingUpdate       = onStreamingUpdate
        };

        await retrievalStage.ExecuteAsync(context, cancellationToken);

        await generationStage.ExecuteAsync(context, cancellationToken);

        await RecordEventAsync
        (
            batch.ProjectID,
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
            roundID,
            EventType.NarrativeOutput,
            context.NarrativeOutput ?? string.Empty,
            cancellationToken
        );

        await RunAuditLoopAsync(context, cancellationToken);

        if (context.AuditPassed)
            await postProcessingStage.ExecuteAsync(context, cancellationToken);

        await directiveRepository.DecrementTTLAsync(batch.ProjectID, cancellationToken);

        Log.Information
        (
            "Orchestrator 批次处理完成: 轮次={RoundID}, 审计通过={Passed}, 违规数={Violations}",
            roundID,
            context.AuditPassed,
            context.Violations.Count
        );

        return new NarrationResult
        (
            context.NarrativeOutput ?? string.Empty,
            context.ThinkingOutput ?? string.Empty,
            roundID,
            context.Violations,
            context.AuditPassed
        );
    }

    public async Task DeleteRoundAsync(long projectID, long roundID, CancellationToken cancellationToken = default)
    {
        Log.Information("删除轮次: 项目={ProjectID}, 轮次={RoundID}", projectID, roundID);
        await eventRepository.RemoveByRoundAsync(roundID, cancellationToken);
    }

    public async Task<NarrationResult> RewriteAsync
    (
        DirectiveBatch          batch,
        Action<string, string>? onStreamingUpdate = null,
        CancellationToken       cancellationToken = default
    )
    {
        var latestRound = await eventRepository.GetLatestRoundIDAsync(batch.ProjectID, cancellationToken);

        if (latestRound > 0)
            await eventRepository.RemoveByRoundAsync(latestRound, cancellationToken);

        return await ProcessBatchAsync(batch, onStreamingUpdate, cancellationToken);
    }

    public async Task<NarrationResult> CorrectAsync
    (
        DirectiveBatch    originalBatch,
        string            correctionGuidance,
        CancellationToken cancellationToken = default
    )
    {
        var latestRound       = await eventRepository.GetLatestRoundIDAsync(originalBatch.ProjectID, cancellationToken);
        var events            = await eventRepository.GetByRoundAsync(latestRound, cancellationToken);
        var narrativeEvent    = events.FirstOrDefault(e => e.Type == EventType.NarrativeOutput);
        var originalNarrative = narrativeEvent?.Data ?? string.Empty;

        var correctedDirectives = originalBatch.Directives.ToList();
        correctedDirectives.Add(new DirectiveItem(DirectiveType.Plot, correctionGuidance, correctedDirectives.Count + 1));

        var correctedBatch = new DirectiveBatch(originalBatch.ProjectID, correctedDirectives);

        await eventRepository.RemoveByRoundAsync(latestRound, cancellationToken);

        return await ProcessBatchAsync(correctedBatch, null, cancellationToken);
    }

    private async Task ProcessDirectivesAsync
    (
        DirectiveBatch    batch,
        long              roundID,
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
                    "添加生效指令: 类型={Type}, TTL={TTL}",
                    directive.Type,
                    ttl?.ToString() ?? "永久"
                );

                await directiveRepository.AddAsync
                (
                    new ActiveDirective
                    {
                        ProjectID = batch.ProjectID,
                        Type      = directive.Type,
                        Content   = directive.Content,
                        TTL       = ttl,
                        CreatedAt = DateTime.UtcNow
                    },
                    cancellationToken
                );
            }

            if (directive.Type == DirectiveType.SceneChange)
                await CreateSceneViaAgentAsync(batch.ProjectID, directive.Content, activeScene, cancellationToken);
        }
    }

    private async Task CreateSceneViaAgentAsync
    (
        long              projectID,
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

    private async Task RecordEventAsync
    (
        long              projectID,
        long              roundID,
        EventType         type,
        string            data,
        CancellationToken cancellationToken
    )
    {
        Log.Debug
        (
            "记录事件: 类型={Type}, 轮次={RoundID}, 数据长度={DataLength}",
            type,
            roundID,
            data.Length
        );

        var eventItem = new PlaythroughEvent
        {
            ProjectID = projectID,
            RoundID   = roundID,
            Type      = type,
            Data      = data,
            CreatedAt = DateTime.UtcNow
        };

        await eventRepository.AppendAsync(eventItem, cancellationToken);
    }
}
