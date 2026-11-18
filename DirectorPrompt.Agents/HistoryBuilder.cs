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
        Scene             scene,
        long              currentRoundID,
        CancellationToken cancellationToken = default
    )
    {
        var config = orchestratorConfig.HistoryContext;

        Log.Debug
        (
            "开始构建叙事历史: 对话={SessionID}, 场景={SceneID}, 进展摘要轮次={ProgressSummaryRoundID}, 当前轮次={CurrentRoundID}, 最大轮次={MaxRounds}, Token预算={TokenBudget}",
            sessionID,
            scene.ID,
            scene.ProgressSummaryRoundID,
            currentRoundID,
            config.MaxRounds,
            config.TokenBudget
        );

        var events = await eventRepository.GetRecentBySceneAsync
                     (
                         sessionID,
                         scene.ID,
                         currentRoundID,
                         Math.Max(config.MaxRounds * 2, 200),
                         scene.ProgressSummaryRoundID,
                         cancellationToken
                     );

        var history    = BuildEntries(events, currentRoundID);
        var usedTokens = history.Sum(EstimateTokens);

        Log.Information
        (
            "叙事历史构建完成: 对话={SessionID}, 场景={SceneID}, 起始轮次={AfterRoundID}, 已选轮次={SelectedRoundCount}, 估算Token={UsedTokens}",
            sessionID,
            scene.ID,
            scene.ProgressSummaryRoundID,
            history.Count,
            usedTokens
        );

        return history;
    }

    public static IReadOnlyList<ChatHistoryEntry> BuildEntries
    (
        IReadOnlyList<PlaythroughEvent> events,
        long                            currentRoundID
    )
    {
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

            if (directorEntry is null
                ||
                narrativeEntry is null)
            {
                continue;
            }

            var directorInput = ParseDirectorInput(directorEntry.Data);
            var narrativeText = narrativeEntry.Data;

            if (!string.IsNullOrWhiteSpace(narrativeText))
            {
                history.Add(new ChatHistoryEntry(roundID, directorInput, narrativeText));
            }
        }

        return history;
    }

    public static string BuildSceneHistoryText(IReadOnlyList<PlaythroughEvent> events)
    {
        var sb = new StringBuilder();

        foreach (var evt in events.OrderBy(e => e.RoundID))
        {
            _ = evt.Type switch
            {
                EventType.DirectorInput   => sb.AppendLine($"[导演指令] {ParseDirectorInput(evt.Data)}"),
                EventType.NarrativeOutput => sb.AppendLine($"[叙事输出] {evt.Data}"),
                _                         => sb
            };
        }

        return sb.ToString();
    }

    public static string BuildSceneHistoryText(IReadOnlyList<ChatHistoryEntry> entries)
    {
        var sb = new StringBuilder();

        foreach (var entry in entries)
        {
            sb.AppendLine($"[导演指令] {entry.DirectorInput}");
            sb.AppendLine($"[叙事输出] {entry.NarrativeOutput}");
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

    public static int EstimateTokens(ChatHistoryEntry entry) =>
        EstimateTokens(entry.DirectorInput) + EstimateTokens(entry.NarrativeOutput);

    private static int EstimateTokens(string text) =>
        (Encoding.UTF8.GetByteCount(text) + 3) / 4;
}
