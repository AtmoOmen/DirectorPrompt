using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Tools;

public sealed class SceneTools
(
    ISceneRepository        sceneRepository,
    ITimelineCalculator     timelineCalculator,
    ISystemStateTransformer systemStateTransformer
)
{
    public IList<AIFunction> Create(ToolExecutionContext context) =>
    [
        AIFunctionFactory.Create
        (
            () => QuerySceneAsync(context),
            "query_scene",
            "查询当前对话的所有场景列表"
        ),
        AIFunctionFactory.Create
        (
            (string timeLabel, long? afterSceneID = null, long? beforeSceneID = null) =>
                CreateSceneAsync(context, afterSceneID, beforeSceneID, timeLabel),
            "create_scene",
            """
            创建新场景, afterSceneID 与 beforeSceneID 至少填一个
            timeLabel: 语义时间标签
            afterSceneID: 新场景在此场景之后
            beforeSceneID: 新场景在此场景之前
            """
        )
    ];

    private async Task<string> QuerySceneAsync(ToolExecutionContext context)
    {
        Log.Information("工具调用: query_scene");

        var scenes = await sceneRepository.GetOrderedByTimelineAsync(context.SessionID);
        var result = scenes.Select
        (s => new
            {
                id               = s.ID,
                timelinePosition = s.TimelinePosition,
                timeLabel        = s.TimeLabel,
                status           = s.Status.ToString()
            }
        );

        return ToolResult.Data(result);
    }

    private async Task<string> CreateSceneAsync
    (
        ToolExecutionContext context,
        long?                afterSceneID,
        long?                beforeSceneID,
        string               timeLabel
    )
    {
        Log.Information("工具调用: create_scene(timeLabel={TimeLabel})", timeLabel);
        if (string.IsNullOrWhiteSpace(timeLabel))
            return ToolResult.Error("timeLabel 不能为空");

        var existingScenes = await sceneRepository.GetOrderedByTimelineAsync(context.SessionID);

        long position;

        try
        {
            position = timelineCalculator.CalculatePosition(afterSceneID, beforeSceneID, existingScenes);
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Error(ex.Message);
        }

        await sceneRepository.CloseActiveSceneAsync(context.SessionID, context.RoundID, null);

        var scene = new Scene
        {
            ProjectID        = context.ProjectID,
            SessionID        = context.SessionID,
            TimelinePosition = position,
            TimeLabel        = timeLabel,
            Status           = SceneStatus.Active
        };

        var created = await sceneRepository.CreateAsync(scene, context.SessionID, context.RoundID);

        await systemStateTransformer.ExecuteAsync
        (
            context.ProjectID,
            context.SessionID,
            created.ID,
            context.RoundID,
            SystemTrigger.SceneChange
        );

        Log.Information("工具调用完成: create_scene, sceneID={ID}, timelinePosition={Position}", created.ID, position);

        return ToolResult.Data(new { sceneID = created.ID, timelinePosition = position });
    }
}
