using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class AuditStage
(
    IChatClientFactory chatClientFactory,
    SceneTools         sceneTools,
    KnowledgeTools     knowledgeTools,
    StateTools         stateTools,
    MemoryTools        memoryTools,
    CharacterTools     characterTools,
    GenerationStage    generationStage,
    OrchestratorConfig orchestratorConfig
)
{
    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var auditConfig = orchestratorConfig.AuditConfig;

        if (auditConfig.Mode == AuditMode.Disabled)
        {
            Log.Information("AuditStage: 审计已禁用, 跳过");
            context.AuditPassed = true;
            return;
        }

        var auditAgent = orchestratorConfig.Agents.FirstOrDefault(a => a.Role == AgentRole.Audit);

        if (auditAgent is null)
        {
            Log.Information("AuditStage: Audit Agent 未启用, 跳过");
            context.AuditPassed = true;
            return;
        }

        var maxRetries = auditConfig.MaxRetries;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            Log.Information("审计循环: 尝试={Attempt}/{MaxRetries}", attempt, maxRetries);

            await AuditAllDimensionsAsync(context, auditAgent, auditConfig, cancellationToken);
            context.AuditRetryCount = attempt;

            if (context.AuditPassed)
            {
                Log.Information("审计通过, 退出循环");
                return;
            }

            if (attempt < maxRetries && auditConfig.Mode == AuditMode.Blocking)
            {
                Log.Warning("审计未通过, 准备重试: 违规数={ViolationCount}", context.Violations.Count);

                await generationStage.RetryWithFeedbackAsync
                (
                    context,
                    context.Violations,
                    cancellationToken
                );
            }
            else
            {
                Log.Warning("达到最大重试次数或非阻塞模式, 强制通过");
                context.AuditPassed = true;
                return;
            }
        }
    }

    private async Task AuditAllDimensionsAsync
    (
        PipelineContext   context,
        AgentDefinition   auditAgent,
        AuditConfig       auditConfig,
        CancellationToken cancellationToken
    )
    {
        var dimensions = auditConfig.Dimensions.Count > 0 ?
                             auditConfig.Dimensions.ToList() :
                             Enum.GetValues<AuditDimension>().ToList();

        Log.Information
        (
            "AuditStage 开始: 模型={Model}, 维度数={DimensionCount}, 维度=[{Dimensions}], 叙事长度={NarrativeLen}",
            auditAgent.ModelConfig.ModelName,
            dimensions.Count,
            string.Join(", ", dimensions),
            context.NarrativeOutput?.Length ?? 0
        );

        var dimensionTasks = dimensions
                             .Select(dim => AuditDimensionAsync(context, auditAgent, dim, cancellationToken))
                             .ToList();

        var dimensionResults = await Task.WhenAll(dimensionTasks);

        var allViolations = dimensionResults
                            .SelectMany(v => v)
                            .Where(v => v.Severity != AuditSeverity.General)
                            .ToList();

        context.Violations.Clear();
        context.Violations.AddRange(allViolations);
        context.AuditPassed = allViolations.Count == 0;

        Log.Information
        (
            "AuditStage 完成: 违规数={ViolationCount}, 审计通过={Passed}",
            allViolations.Count,
            context.AuditPassed
        );

        foreach (var v in allViolations)
        {
            Log.Warning
            (
                "审计违规: 类型={Type}, 严重性={Severity}, 描述={Description}",
                v.Type,
                v.Severity,
                v.Description
            );
        }
    }

    private async Task<IReadOnlyList<Violation>> AuditDimensionAsync
    (
        PipelineContext   context,
        AgentDefinition   auditAgent,
        AuditDimension    dimension,
        CancellationToken cancellationToken
    )
    {
        Log.Information("审计维度 {Dimension} 开始", dimension);

        var dimensionAuditTools = new AuditTools();

        var (prompt, tools) = GetDimensionConfig(dimension, context.ToolContext, dimensionAuditTools);
        var client = chatClientFactory.Create(auditAgent.ModelConfig);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, prompt),
            new(ChatRole.User, context.NarrativeOutput ?? string.Empty)
        };

        var options = new ChatOptions
        {
            Temperature = auditAgent.Temperature,
            ModelId     = auditAgent.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        await client.GetResponseAsync(messages, options, cancellationToken);

        var violations = dimensionAuditTools.Violations;
        Log.Information("审计维度 {Dimension} 完成: 违规数={ViolationCount}", dimension, violations.Count);

        return violations;
    }

    private (string prompt, IList<AIFunction> tools) GetDimensionConfig
    (
        AuditDimension       dimension,
        ToolExecutionContext context,
        AuditTools           auditTools
    ) =>
        dimension switch
        {
            AuditDimension.Setting => (
                                          AuditAgentPrompt.SETTING,
                                          [..knowledgeTools.Create(context), ..auditTools.Create()]
                                      ),
            AuditDimension.State => (
                                        AuditAgentPrompt.STATE,
                                        [..stateTools.Create(context), ..characterTools.Create(context), ..auditTools.Create()]
                                    ),
            AuditDimension.Character => (
                                            AuditAgentPrompt.CHARACTER,
                                            [..characterTools.Create(context), ..auditTools.Create()]
                                        ),
            AuditDimension.Time => (
                                       AuditAgentPrompt.TIME,
                                       [..sceneTools.Create(context), ..auditTools.Create()]
                                   ),
            AuditDimension.Memory => (
                                         AuditAgentPrompt.MEMORY,
                                         [..memoryTools.Create(context), ..auditTools.Create()]
                                     ),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
}
