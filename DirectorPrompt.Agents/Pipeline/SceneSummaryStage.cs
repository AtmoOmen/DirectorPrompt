using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class SceneSummaryStage
(
    IChatClientFactory  chatClientFactory,
    AgentConfigResolver agentConfigResolver,
    ISceneRepository    sceneRepository,
    IEventRepository    eventRepository
)
{
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

        var events = await eventRepository.GetBySceneAsync(sessionID, sceneID, cancellationToken);

        var historyText = HistoryBuilder.BuildSceneHistoryText(events);

        if (string.IsNullOrWhiteSpace(historyText))
            return;

        var resolved = agentConfigResolver.Resolve(AgentTaskType.Narrator);

        if (resolved is null)
        {
            Log.Warning("场景摘要生成跳过: Narrator Agent 未配置");
            return;
        }

        Log.Information("场景摘要生成: 场景={SceneID}, 事件数={EventCount}", sceneID, events.Count);

        var client = chatClientFactory.Create(resolved.ProviderConfig, resolved.ModelConfig);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SceneSummaryPrompt.SYSTEM),
            new(ChatRole.User,   historyText)
        };

        var options = new ChatOptions
        {
            Temperature = 0.3f,
            ModelId     = resolved.ModelConfig.ModelName
        };

        try
        {
            var response = await client.GetResponseAsync(messages, options, cancellationToken);
            var summary  = response.Text?.Trim();

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
}
