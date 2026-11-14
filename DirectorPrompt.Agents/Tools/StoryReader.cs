using System.ComponentModel;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;

namespace DirectorPrompt.Agents.Tools;

public sealed class StoryReader
(
    IEventRepository eventRepository,
    ISceneRepository sceneRepository,
    PipelineContext  context
)
{
    public IReadOnlyList<AIFunction> CreateTools() =>
    [
        AIFunctionFactory.Create
        (
            ReadRoundAsync,
            "read_story_round",
            "读取已完成轮次的故事原文及当轮导演指令。roundID=0 读取开场，其他轮次 ID 取自历史工具结果或 list_story_rounds。支持按行分页，可读取已移出上下文的旧轮次；不读取本轮草稿，也不读取当前时间线之后的故事。"
        ),
        AIFunctionFactory.Create
            (ListRoundsAsync, "list_story_rounds", "分页列出场景内已完成的故事轮次，返回轮次 ID、导演指令和正文行数，不返回正文。默认当前场景，按轮次从新到旧排列；将 nextBeforeRoundID 传入 beforeRoundID 可继续向前翻页。"),
        AIFunctionFactory.Create(ListScenesAsync, "list_story_scenes", "列出当前故事时间位置及之前的场景 ID 和时间标签，用于指定 list_story_rounds 的 sceneID，不返回故事正文。")
    ];

    public static string FormatRound
    (
        long   roundID,
        long?  sceneID,
        string directorInput,
        string text,
        int    startLine = 1,
        int    lineCount = 0
    )
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        if (startLine < 1 || startLine > lines.Length || lineCount < 0)
            throw new ArgumentException("行范围无效，请根据 totalLines 指定范围。");

        var count = lineCount == 0 ?
                        lines.Length - startLine + 1 :
                        Math.Min(lineCount, lines.Length - startLine + 1);
        var endLine = startLine + count - 1;

        return ToolResult.Data
        (
            new
            {
                roundID,
                sceneID,
                directorInput,
                totalLines = lines.Length,
                startLine,
                endLine,
                nextStartLine = endLine < lines.Length ?
                                    (int?)(endLine + 1) :
                                    null,
                text = string.Join('\n', lines.Skip(startLine - 1).Take(count).Select((line, index) => $"{startLine + index}: {line}"))
            }
        );
    }

    private async Task<string> ReadRoundAsync
    (
        [Description("故事轮次 ID；0 表示开场，必须小于当前正在创作的轮次。")]
        long roundID,
        [Description("起始行，包含该行，从 1 开始。")] int startLine = 1,
        [Description("读取行数，默认 200；0 表示读取至文末。")]
        int lineCount = 200,
        CancellationToken cancellationToken = default
    )
    {
        if (roundID < 0 || roundID >= context.RoundID)
            throw new ArgumentException("只能读取当前创作轮次之前的故事，使用 read_story 查看本轮草稿。");

        if (roundID == 0)
        {
            var opening = context.Project?.OpeningMessage;

            return string.IsNullOrWhiteSpace(opening) ?
                       throw new ArgumentException("项目未设置开场叙事。") :
                       FormatRound(0, null, string.Empty, opening, startLine, lineCount);
        }

        var events    = await eventRepository.GetByRoundAsync(context.SessionID, roundID, cancellationToken);
        var narrative = events.Where(item => item.Type == EventType.NarrativeOutput).MaxBy(item => item.ID);

        if (narrative?.SceneID is not { } sceneID)
            throw new ArgumentException("该轮次没有已完成的故事正文。");

        await GetVisibleSceneAsync(sceneID, cancellationToken);

        var director = events.Where(item => item.Type == EventType.DirectorInput).MaxBy(item => item.ID);
        var directorInput = director is null ?
                                string.Empty :
                                HistoryBuilder.ParseDirectorInput(director.Data);

        return FormatRound(roundID, sceneID, directorInput, narrative.Data, startLine, lineCount);
    }

    private async Task<string> ListRoundsAsync
    (
        [Description("场景 ID，省略时使用当前场景；通过 list_story_scenes 查询其他可读场景。")]
        long? sceneID = null,
        [Description("只列出小于该 ID 的轮次，省略时从当前创作轮次之前开始。")]
        long? beforeRoundID = null,
        [Description("每页轮次数，1 至 100，默认 20。")] int maxRounds         = 20,
        CancellationToken                         cancellationToken = default
    )
    {
        if (maxRounds is < 1 or > 100)
            throw new ArgumentException("maxRounds 必须为 1 至 100。");

        var before = beforeRoundID ?? context.RoundID;

        if (before < 1 || before > context.RoundID)
            throw new ArgumentException("beforeRoundID 必须大于 0，且不超过当前创作轮次。");

        var selectedSceneID = sceneID ?? context.CurrentSceneID ?? throw new ArgumentException("当前没有场景，请指定 sceneID。");
        var scene           = await GetVisibleSceneAsync(selectedSceneID, cancellationToken);

        var events = await eventRepository.GetRecentBySceneAsync
                     (
                         context.SessionID,
                         selectedSceneID,
                         before,
                         maxRounds + 1,
                         cancellationToken: cancellationToken
                     );
        var rounds   = events.GroupBy(item => item.RoundID).OrderByDescending(group => group.Key).ToList();
        var selected = rounds.Take(maxRounds).ToList();

        return ToolResult.Data
        (
            new
            {
                sceneID        = selectedSceneID,
                timeLabel      = scene.TimeLabel,
                currentRoundID = context.RoundID,
                nextBeforeRoundID = rounds.Count > maxRounds ?
                                        (long?)selected[^1].Key :
                                        null,
                rounds = selected.Select
                (group =>
                    {
                        var director  = group.Where(item => item.Type == EventType.DirectorInput).MaxBy(item => item.ID);
                        var narrative = group.Where(item => item.Type == EventType.NarrativeOutput).MaxBy(item => item.ID);

                        return new
                        {
                            roundID = group.Key,
                            directorInput = director is null ?
                                                string.Empty :
                                                HistoryBuilder.ParseDirectorInput(director.Data),
                            hasStory   = narrative is not null,
                            totalLines = narrative?.Data.Count(character => character == '\n') + 1 ?? 0
                        };
                    }
                )
            }
        );
    }

    private async Task<string> ListScenesAsync(CancellationToken cancellationToken = default)
    {
        var scenes = await sceneRepository.GetOrderedByTimelineAsync(context.SessionID, cancellationToken);

        return ToolResult.Data
        (
            new
            {
                currentRoundID = context.RoundID,
                currentSceneID = context.CurrentSceneID,
                hasOpening     = !string.IsNullOrWhiteSpace(context.Project?.OpeningMessage),
                scenes = scenes.Where(scene => scene.TimelinePosition <= context.CurrentTimelinePosition)
                               .Select(scene => new { sceneID = scene.ID, timeLabel = scene.TimeLabel, timelinePosition = scene.TimelinePosition })
            }
        );
    }

    private async Task<Scene> GetVisibleSceneAsync(long sceneID, CancellationToken cancellationToken)
    {
        var scene = await sceneRepository.GetByIDAsync(sceneID, cancellationToken);

        if (scene is null || scene.SessionID != context.SessionID || scene.TimelinePosition > context.CurrentTimelinePosition)
            throw new ArgumentException("场景不存在，或不在当前对话的可读时间范围内。");

        return scene;
    }
}
