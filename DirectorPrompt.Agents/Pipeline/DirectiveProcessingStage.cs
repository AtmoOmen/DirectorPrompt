using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class DirectiveProcessingStage
(
    IChatClientFactory   chatClientFactory,
    SceneTools           sceneTools,
    ISceneRepository     sceneRepository,
    IDirectiveRepository directiveRepository,
    ITimelineCalculator  timelineCalculator,
    OrchestratorConfig   orchestratorConfig
)
{
    public async Task ExecuteAsync
    (
        DirectiveBatch    batch,
        long              sessionID,
        Scene?            activeScene,
        ModelConfig       embeddingConfig,
        CancellationToken cancellationToken
    )
    {
        if (activeScene is null)
        {
            var sceneChangeDirective = batch.Directives.FirstOrDefault(d => d.Type == DirectiveType.SceneChange);

            if (sceneChangeDirective is not null)
            {
                Log.Information("无活跃场景, 通过 Scene Agent 创建: {Description}", sceneChangeDirective.Content);
                await CreateSceneViaAgentAsync
                (
                    batch.ProjectID,
                    sessionID,
                    sceneChangeDirective.Content,
                    activeScene,
                    embeddingConfig,
                    cancellationToken
                );
            }
            else
            {
                Log.Information("无活跃场景且无 SceneChange 指令, 直接创建初始场景");

                var existingScenes = await sceneRepository.GetOrderedByTimelineAsync(sessionID, cancellationToken);
                var position       = timelineCalculator.CalculatePosition(null, null, existingScenes);

                await sceneRepository.CreateAsync
                (
                    new Scene
                    {
                        ProjectID        = batch.ProjectID,
                        SessionID        = sessionID,
                        TimelinePosition = position,
                        TimeLabel        = "初始场景",
                        Status           = SceneStatus.Active
                    },
                    cancellationToken
                );
            }
        }

        foreach (var directive in batch.Directives)
        {
            switch (directive.Type)
            {
                case DirectiveType.Tone or DirectiveType.TemporaryConstraint:
                    Log.Information
                    (
                        "添加生效指令: 类型={Type}, 内容={Content}, TTL={TTL}",
                        directive.Type,
                        directive.Content,
                        directive.TTL?.ToString() ?? "永久"
                    );

                    await directiveRepository.AddAsync
                    (
                        new ActiveDirective
                        {
                            ProjectID = batch.ProjectID,
                            SessionID = sessionID,
                            Type      = directive.Type,
                            Content   = directive.Content,
                            TTL       = directive.TTL,
                            CreatedAt = DateTime.UtcNow
                        },
                        cancellationToken
                    );
                    break;

                case DirectiveType.SceneChange:
                    await CreateSceneViaAgentAsync
                    (
                        batch.ProjectID,
                        sessionID,
                        directive.Content,
                        activeScene,
                        embeddingConfig,
                        cancellationToken
                    );
                    break;
            }
        }
    }

    private async Task CreateSceneViaAgentAsync
    (
        long              projectID,
        long              sessionID,
        string            description,
        Scene?            currentScene,
        ModelConfig       embeddingConfig,
        CancellationToken cancellationToken
    )
    {
        var sceneAgent = orchestratorConfig.Agents.FirstOrDefault(a => a.Role == AgentRole.Scene);

        if (sceneAgent is null)
        {
            Log.Debug("Scene Agent 为空, 跳过场景创建");
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
            0,
            embeddingConfig
        );

        var client = chatClientFactory.Create(sceneAgent.ModelConfig);
        var tools  = sceneTools.Create(toolContext);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SceneAgentPrompt.SYSTEM),
            new(ChatRole.User, description)
        };

        var options = new ChatOptions
        {
            Temperature = sceneAgent.Temperature,
            ModelId     = sceneAgent.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        const int MAX_SCENE_RETRIES = 5;

        for (var attempt = 1; attempt <= MAX_SCENE_RETRIES; attempt++)
        {
            var response = await client.GetResponseAsync(messages, options, cancellationToken);

            var responseText = response.Messages.FirstOrDefault()?.Text ?? "(空)";

            Log.Information
            (
                "Scene Agent 返回 (尝试 {Attempt}/{MaxRetries}): {Text}",
                attempt,
                MAX_SCENE_RETRIES,
                responseText.Length > 200 ?
                    responseText[..200] + "..." :
                    responseText
            );

            var sceneAfterAgent = await sceneRepository.GetActiveSceneAsync(sessionID, cancellationToken);

            if (sceneAfterAgent is not null)
            {
                Log.Information("场景创建完成: sceneID={SceneID}", sceneAfterAgent.ID);
                return;
            }

            Log.Warning
            (
                "Scene Agent 未调用 create_scene 工具, 重试 {Attempt}/{MaxRetries}",
                attempt,
                MAX_SCENE_RETRIES
            );

            if (attempt < MAX_SCENE_RETRIES)
            {
                messages =
                [
                    new
                    (
                        ChatRole.System,
                        SceneAgentPrompt.SYSTEM + "\n\n注意: 你之前没有调用 create_scene 工具, 这是强制要求。请立即调用 create_scene 工具创建场景, 不要只回复文本。"
                    ),

                    new(ChatRole.User, description)
                ];
            }
        }
    }
}
