using System.Text;
using System.Text.Json;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Serilog;

namespace DirectorPrompt.Agents;

public sealed class HistoryBuilder
(
    IEventRepository   eventRepository,
    OrchestratorConfig orchestratorConfig
)
{
    public async Task<IReadOnlyList<ChatHistoryEntry>> BuildAsync
    (
        long              sessionID,
        long              sceneID,
        long              currentRoundID,
        CancellationToken cancellationToken = default
    )
    {
        var config = orchestratorConfig.HistoryContext;

        Log.Debug
        (
            "开始构建叙事历史: 对话={SessionID}, 场景={SceneID}, 当前轮次={CurrentRoundID}, 最大轮次={MaxRounds}, Token预算={TokenBudget}",
            sessionID,
            sceneID,
            currentRoundID,
            config.MaxRounds,
            config.TokenBudget
        );

        var events = await eventRepository.GetRecentBySceneAsync
                     (
                         sessionID,
                         sceneID,
                         currentRoundID,
                         config.MaxRounds,
                         cancellationToken
                     );

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

        var selected   = new List<ChatHistoryEntry>();
        var usedTokens = 0;

        for (var index = history.Count - 1; index >= 0; index--)
        {
            var entry  = history[index];
            var tokens = EstimateTokens(entry.DirectorInput) + EstimateTokens(entry.NarrativeOutput);

            if (selected.Count > 0 && usedTokens + tokens > config.TokenBudget)
                break;

            selected.Add(entry);
            usedTokens += tokens;
        }

        selected.Reverse();

        Log.Information
        (
            "叙事历史构建完成: 对话={SessionID}, 场景={SceneID}, 候选轮次={CandidateRoundCount}, 已选轮次={SelectedRoundCount}, 估算Token={UsedTokens}",
            sessionID,
            sceneID,
            history.Count,
            selected.Count,
            usedTokens
        );

        return selected;
    }

    public static string BuildSceneHistoryText(IReadOnlyList<PlaythroughEvent> events)
    {
        var sb = new StringBuilder();

        foreach (var evt in events.OrderBy(e => e.RoundID))
        {
            if (evt.Type == EventType.DirectorInput)
                sb.AppendLine($"[导演指令] {ParseDirectorInput(evt.Data)}");
            else if (evt.Type == EventType.NarrativeOutput)
                sb.AppendLine($"[叙事输出] {evt.Data}");
        }

        return sb.ToString();
    }

    public static string ParseDirectorInput(string json)
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
        catch (JsonException exception)
        {
            Log.Warning(exception, "解析导演指令历史失败, 将使用原始数据: 数据长度={DataLength}", json.Length);
            return json;
        }
    }

    private static int EstimateTokens(string text) =>
        (Encoding.UTF8.GetByteCount(text) + 3) / 4;
}
