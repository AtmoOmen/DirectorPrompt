using DirectorPrompt.Agents.Config;
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
    public async Task ExecuteAsync
    (
        long              sessionID,
        long              roundID,
        long              sceneID,
        CancellationToken cancellationToken = default
    )
    {
        var scene = await sceneRepository.GetByIDAsync(sceneID, cancellationToken);

        if (scene is null)
        {
            return;
        }

        var events = await eventRepository.GetRecentBySceneAsync
                     (
                         sessionID,
                         sceneID,
                         long.MaxValue,
                         Math.Max(orchestratorConfig.HistoryContext.MaxRounds * 2, 200),
                         scene.ProgressSummaryRoundID,
                         cancellationToken
                     );

        var historyText = HistoryBuilder.BuildSceneHistoryText(events);

        var input = (string.IsNullOrWhiteSpace(scene.ProgressSummary), string.IsNullOrWhiteSpace(historyText)) switch
        {
            (false, false) => $"[既有进展摘要]\n{scene.ProgressSummary}\n\n[最近历史]\n{historyText}",
            (false, true)  => scene.ProgressSummary!,
            (true, false)  => historyText,
            (true, true)   => string.Empty
        };

        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var resolved = agentConfigResolver.Resolve(AgentTaskType.Narrator);

        if (resolved is null)
        {
            Log.Warning("场景摘要生成跳过: Narrator Agent 未配置");

            return;
        }

        Log.Information("场景摘要生成: 场景={SceneID}, 事件数={EventCount}", sceneID, events.Count);

        try
        {
            var summary = await GenerateSummaryAsync(resolved, input, cancellationToken);

            if (!string.IsNullOrWhiteSpace(summary))
            {
                await sceneRepository.UpdateAsync(scene with { Summary = summary }, sessionID, roundID, cancellationToken);
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
        var events = await eventRepository.GetRecentBySceneAsync
                     (
                         sessionID,
                         scene.ID,
                         currentRoundID,
                         Math.Max(historyConfig.MaxRounds * 2, 200),
                         scene.ProgressSummaryRoundID,
                         cancellationToken
                     );
        var entries     = HistoryBuilder.BuildEntries(events, currentRoundID);
        var totalTokens = entries.Sum(HistoryBuilder.EstimateTokens);

        if (entries.Count <= 1
            ||
            (entries.Count <= historyConfig.MaxRounds
             &&
             totalTokens <= historyConfig.TokenBudget))
        {
            return scene;
        }

        var resolved = agentConfigResolver.Resolve(AgentTaskType.Narrator);

        if (resolved is null)
        {
            return scene;
        }

        var targetRounds   = Math.Max(1, historyConfig.MaxRounds   / 2);
        var targetTokens   = Math.Max(1, historyConfig.TokenBudget / 2);
        var retainedRounds = 0;
        var retainedTokens = 0;
        var splitIndex     = entries.Count - 1;

        for (var index = entries.Count - 1; index >= 1; index--)
        {
            var entryTokens = HistoryBuilder.EstimateTokens(entries[index]);

            if (retainedRounds > 0
                &&
                (retainedRounds + 1 > targetRounds
                 ||
                 retainedTokens + entryTokens > targetTokens))
            {
                splitIndex = index + 1;
                break;
            }

            retainedRounds++;
            retainedTokens += entryTokens;
            splitIndex     =  index;
        }

        var chunk = entries.Take(splitIndex).ToList();

        try
        {
            var historyText = HistoryBuilder.BuildSceneHistoryText(chunk);
            var input = string.IsNullOrWhiteSpace(scene.ProgressSummary) ?
                            historyText :
                            $"[既有进展摘要]\n{scene.ProgressSummary}\n\n[新增历史]\n{historyText}";
            var summary = await GenerateSummaryAsync(resolved, input, cancellationToken);

            if (string.IsNullOrWhiteSpace(summary))
            {
                return scene;
            }

            var throughRoundID = chunk[^1].RoundID;
            await sceneRepository.UpdateProgressSummaryAsync(scene.ID, summary, throughRoundID, sessionID, currentRoundID, cancellationToken);

            Log.Information
            (
                "场景阶段进展摘要已压缩: 场景={SceneID}, 压缩轮次数={CompactedCount}, 保留轮次数={RetainedCount}, 截止轮次={ThroughRoundID}",
                scene.ID,
                chunk.Count,
                entries.Count - chunk.Count,
                throughRoundID
            );

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

        return response.Text.Trim();
    }
}
