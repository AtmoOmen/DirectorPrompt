using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class SceneSummaryStage
(
    IChatClientFactory  chatClientFactory,
    AgentConfigResolver agentConfigResolver,
    ISceneRepository    sceneRepository,
    IEventRepository    eventRepository,
    OrchestratorConfig  orchestratorConfig
)
{
    private const int SUMMARY_CHUNK_SIZE = 20;

    public async Task ExecuteAsync
    (
        long              sessionID,
        long              sceneID,
        CancellationToken cancellationToken = default
    )
    {
        var scene = await sceneRepository.GetByIDAsync(sceneID, cancellationToken);

        if (scene is null)
            return;

        var events = await eventRepository.GetRecentBySceneAsync
                     (
                         sessionID,
                         sceneID,
                         long.MaxValue,
                         orchestratorConfig.HistoryContext.MaxRounds,
                         cancellationToken
                     );

        var historyText = HistoryBuilder.BuildSceneHistoryText(events);

        if (!string.IsNullOrWhiteSpace(scene.ProgressSummary))
            historyText = $"[既有进展摘要]\n{scene.ProgressSummary}\n\n[最近历史]\n{historyText}";

        if (string.IsNullOrWhiteSpace(historyText))
            return;

        var resolved = agentConfigResolver.Resolve(AgentTaskType.Narrator);

        if (resolved is null)
        {
            Log.Warning("场景摘要生成跳过: Narrator Agent 未配置");
            return;
        }

        Log.Information("场景摘要生成: 场景={SceneID}, 事件数={EventCount}", sceneID, events.Count);

        try
        {
            var summary = await GenerateSummaryAsync(resolved, historyText, cancellationToken);

            if (!string.IsNullOrWhiteSpace(summary))
            {
                await sceneRepository.UpdateAsync(scene with { Summary = summary }, cancellationToken);
                Log.Information("场景摘要生成完成: 场景={SceneID}, 摘要长度={SummaryLen}", sceneID, summary.Length);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "场景摘要生成失败: 场景={SceneID}", sceneID);
        }
    }

    public async Task<Scene> UpdateProgressSummaryAsync
    (
        long              sessionID,
        Scene             scene,
        long              currentRoundID,
        CancellationToken cancellationToken = default
    )
    {
        var historyConfig = orchestratorConfig.HistoryContext;
        var events = await eventRepository.GetSceneSummaryChunkAsync
                     (
                         sessionID,
                         scene.ID,
                         scene.ProgressSummaryRoundID,
                         currentRoundID,
                         historyConfig.MaxRounds,
                         SUMMARY_CHUNK_SIZE,
                         cancellationToken
                     );
        var roundIDs = events.Select(item => item.RoundID).Distinct().ToList();

        if (roundIDs.Count < SUMMARY_CHUNK_SIZE)
            return scene;

        var resolved = agentConfigResolver.Resolve(AgentTaskType.Narrator);

        if (resolved is null)
            return scene;

        try
        {
            var historyText = HistoryBuilder.BuildSceneHistoryText(events);
            var input = string.IsNullOrWhiteSpace(scene.ProgressSummary) ?
                            historyText :
                            $"[既有进展摘要]\n{scene.ProgressSummary}\n\n[新增历史]\n{historyText}";
            var summary = await GenerateSummaryAsync(resolved, input, cancellationToken);

            if (string.IsNullOrWhiteSpace(summary))
                return scene;

            var throughRoundID = roundIDs[^1];
            await sceneRepository.UpdateProgressSummaryAsync(scene.ID, summary, throughRoundID, cancellationToken);
            return scene with { ProgressSummary = summary, ProgressSummaryRoundID = throughRoundID };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "场景进展摘要更新失败: 场景={SceneID}", scene.ID);
            return scene;
        }
    }

    private async Task<string?> GenerateSummaryAsync
    (
        ResolvedAgentTask resolved,
        string            historyText,
        CancellationToken cancellationToken
    )
    {
        var client = chatClientFactory.Create(resolved.ProviderConfig, resolved.ModelConfig);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SceneSummaryPrompt.SYSTEM),
            new(ChatRole.User, historyText)
        };
        var options = new ChatOptions
        {
            Temperature = 0.3f,
            ModelId     = resolved.ModelConfig.ModelName
        };
        var response = await client.GetResponseAsync(messages, options, cancellationToken);
        return response.Text?.Trim();
    }
}
