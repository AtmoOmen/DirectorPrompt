using System.Text;
using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class GenerationStage
{
    private readonly IChatClientFactory chatClientFactory;
    private readonly KnowledgeTools     knowledgeTools;
    private readonly OrchestratorConfig orchestratorConfig;

    public GenerationStage
    (
        IChatClientFactory chatClientFactory,
        KnowledgeTools     knowledgeTools,
        OrchestratorConfig orchestratorConfig
    )
    {
        this.chatClientFactory  = chatClientFactory;
        this.knowledgeTools     = knowledgeTools;
        this.orchestratorConfig = orchestratorConfig;
    }

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var narratorAgent = orchestratorConfig.Agents.FirstOrDefault(a => a.Role == AgentRole.Narrator);

        if (narratorAgent is null)
            throw new InvalidOperationException("未配置 Narrator Agent");

        Log.Information
        (
            "GenerationStage 开始: 模型={Model}, 温度={Temperature}",
            narratorAgent.ModelConfig.ModelName,
            narratorAgent.Temperature
        );

        var client      = chatClientFactory.Create(narratorAgent.ModelConfig);
        var tools       = knowledgeTools.Create(context.ToolContext);
        var userMessage = BuildNarratorInput(context);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, NarratorPrompt.System),
            new(ChatRole.User, userMessage)
        };

        var options = new ChatOptions
        {
            Temperature = narratorAgent.Temperature,
            ModelId     = narratorAgent.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        var narrativeBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var updateCount      = 0;

        var updates = client.GetStreamingResponseAsync(messages, options, cancellationToken);

        await foreach (var update in updates)
        {
            updateCount++;

            foreach (var content in update.Contents)
            {
                if (content is TextReasoningContent reasoning)
                    reasoningBuilder.Append(reasoning.Text);
                else if (content is TextContent text)
                    narrativeBuilder.Append(text.Text);
            }

            if (context.OnStreamingUpdate is not null)
            {
                context.OnStreamingUpdate
                (
                    narrativeBuilder.ToString(),
                    reasoningBuilder.ToString()
                );
            }
        }

        var apiReasoning = reasoningBuilder.ToString();
        var rawText      = narrativeBuilder.ToString();
        var (thinking, narrative) = ThinkingParser.Merge(apiReasoning, rawText);

        context.NarrativeOutput = narrative;
        context.ThinkingOutput  = thinking;

        Log.Information
        (
            "GenerationStage 完成: 流式更新数={Updates}, 叙事长度={NarrativeLen}, 思考长度={ThinkingLen}",
            updateCount,
            narrative.Length,
            thinking.Length
        );

        if (!string.IsNullOrEmpty(thinking))
            Log.Debug("Thinking 内容预览: {Preview}", thinking.Length > 200 ? thinking[..200] + "..." : thinking);
    }

    public async Task RetryWithFeedbackAsync
    (
        PipelineContext          context,
        IReadOnlyList<Violation> violations,
        CancellationToken        cancellationToken = default
    )
    {
        var narratorAgent = orchestratorConfig.Agents.FirstOrDefault(a => a.Role == AgentRole.Narrator);

        if (narratorAgent is null)
            throw new InvalidOperationException("未配置 Narrator Agent");

        Log.Information
        (
            "GenerationStage 重试: 模型={Model}, 违规数={ViolationCount}",
            narratorAgent.ModelConfig.ModelName,
            violations.Count
        );

        var client = chatClientFactory.Create(narratorAgent.ModelConfig);
        var tools  = knowledgeTools.Create(context.ToolContext);

        var sb = new StringBuilder();
        sb.AppendLine("## 上一次输出存在以下问题, 请修正后重新生成:");
        sb.AppendLine();

        foreach (var violation in violations)
        {
            sb.AppendLine($"- [{violation.Severity}] {violation.Description}");

            if (!string.IsNullOrWhiteSpace(violation.Suggestion))
                sb.AppendLine($"  建议: {violation.Suggestion}");
        }

        sb.AppendLine();
        sb.AppendLine("## 原始输出:");
        sb.AppendLine(context.NarrativeOutput);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, NarratorPrompt.System),
            new(ChatRole.User, BuildNarratorInput(context)),
            new(ChatRole.Assistant, context.NarrativeOutput ?? string.Empty),
            new(ChatRole.User, sb.ToString())
        };

        var options = new ChatOptions
        {
            Temperature = narratorAgent.Temperature,
            ModelId     = narratorAgent.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        var response = await client.GetResponseAsync(messages, options, cancellationToken);

        var assistantMessage = response.Messages.LastOrDefault();

        var apiReasoning = ExtractReasoning(assistantMessage);
        var rawText      = assistantMessage?.Text ?? string.Empty;
        var (thinking, narrative) = ThinkingParser.Merge(apiReasoning, rawText);

        context.NarrativeOutput = narrative;
        context.ThinkingOutput  = thinking;

        Log.Information
        (
            "GenerationStage 重试完成: 叙事长度={NarrativeLen}, 思考长度={ThinkingLen}",
            narrative.Length,
            thinking.Length
        );
    }

    private static string ExtractReasoning(ChatMessage? message)
    {
        if (message is null)
            return string.Empty;

        var sb = new StringBuilder();

        foreach (var content in message.Contents)
        {
            if (content is TextReasoningContent reasoning)
                sb.Append(reasoning.Text);
        }

        return sb.ToString();
    }

    private static string BuildNarratorInput(PipelineContext context)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(context.SystemInjection))
            sb.AppendLine(context.SystemInjection);

        if (!string.IsNullOrWhiteSpace(context.KnowledgeContext))
        {
            sb.AppendLine("## 知识上下文");
            sb.AppendLine(context.KnowledgeContext);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(context.MemoryContext))
        {
            sb.AppendLine("## 记忆上下文");
            sb.AppendLine(context.MemoryContext);
            sb.AppendLine();
        }

        sb.AppendLine("## 导演指令");
        foreach (var item in context.DirectiveBatch.Directives)
            sb.AppendLine($"{item.Order}. [{item.Type}] {item.Content}");

        return sb.ToString();
    }
}
