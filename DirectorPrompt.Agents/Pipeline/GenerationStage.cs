using System.Text;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Enums;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class GenerationStage
(
    IChatClientFactory  chatClientFactory,
    AgentConfigResolver agentConfigResolver,
    KnowledgeTools      knowledgeTools
)
{
    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var resolved = agentConfigResolver.Resolve(AgentTaskType.Narrator);

        if (resolved is null)
            throw new InvalidOperationException("未配置 Narrator Agent");

        Log.Information
        (
            "GenerationStage 开始: 模型={Model}, 温度={Temperature}",
            resolved.ModelConfig.ModelName,
            resolved.ModelConfig.Temperature
        );

        var client       = chatClientFactory.Create(resolved.ProviderConfig, resolved.ModelConfig);
        var tools        = knowledgeTools.Create(context.ToolContext);
        var systemPrompt = BuildSystemPrompt(context, resolved.SystemPrompt);
        var userMessage  = BuildNarratorInput(context);

        Log.Information
        (
            "Narrator 输入:\n{Input}",
            userMessage
        );

        var messages = BuildMessages(systemPrompt, resolved.ModelPrompt, context);

        messages.Add(new ChatMessage(ChatRole.User, userMessage));

        var options = new ChatOptions
        {
            Temperature = resolved.ModelConfig.Temperature,
            ModelId     = resolved.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        var narrativeBuilder   = new StringBuilder();
        var reasoningBuilder   = new StringBuilder();
        var updateCount        = 0;
        var streamingAttempted = context.OnStreamingUpdate is not null;

        if (streamingAttempted)
        {
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

                context.OnStreamingUpdate?.Invoke
                (
                    narrativeBuilder.ToString(),
                    reasoningBuilder.ToString()
                );
            }
        }

        var apiReasoning = reasoningBuilder.ToString();
        var rawText      = narrativeBuilder.ToString();

        if (!streamingAttempted || string.IsNullOrWhiteSpace(rawText))
        {
            if (streamingAttempted)
            {
                Log.Warning
                (
                    "流式响应叙事文本为空, 回退到非流式: 流式更新数={Updates}",
                    updateCount
                );

                narrativeBuilder.Clear();
                reasoningBuilder.Clear();
            }

            var response         = await client.GetResponseAsync(messages, options, cancellationToken);
            var assistantMessage = response.Messages.LastOrDefault();

            apiReasoning = ExtractReasoning(assistantMessage);
            rawText      = assistantMessage?.Text ?? string.Empty;

            context.OnStreamingUpdate?.Invoke(rawText, apiReasoning);
        }

        var (thinking, narrative) = ThinkingParser.Merge(apiReasoning, rawText);

        context.NarrativeOutput = narrative;
        context.ThinkingOutput  = thinking;

        Log.Information
        (
            "GenerationStage 完成: 叙事长度={NarrativeLen}, 思考长度={ThinkingLen}",
            narrative.Length,
            thinking.Length
        );

        Log.Information("Narrator 叙事输出:\n{Narrative}", narrative);

        if (!string.IsNullOrEmpty(thinking))
            Log.Debug("Narrator 思考内容:\n{Thinking}", thinking);
    }

    private static List<ChatMessage> BuildMessages(string systemPrompt, string? modelPrompt, PipelineContext context)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(modelPrompt))
            messages.Add(new ChatMessage(ChatRole.System, modelPrompt));

        messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

        foreach (var entry in context.History)
        {
            messages.Add(new ChatMessage(ChatRole.User,      entry.DirectorInput));
            messages.Add(new ChatMessage(ChatRole.Assistant, entry.NarrativeOutput));
        }

        return messages;
    }

    private static string BuildSystemPrompt(PipelineContext context, string basePrompt)
    {
        var sb = new StringBuilder();

        sb.AppendLine(basePrompt);

        if (context.Project is not null)
        {
            if (!string.IsNullOrWhiteSpace(context.Project.Description))
            {
                sb.AppendLine();
                sb.AppendLine("## 项目设定");
                sb.AppendLine(context.Project.Description);
            }

            if (!string.IsNullOrWhiteSpace(context.Project.OpeningMessage))
            {
                sb.AppendLine();
                sb.AppendLine("## 开场叙事");
                sb.AppendLine(context.Project.OpeningMessage);
            }
        }

        if (!string.IsNullOrWhiteSpace(context.PreviousSceneSummary))
        {
            sb.AppendLine();
            sb.AppendLine("## 上一场景摘要");
            sb.AppendLine(context.PreviousSceneSummary);
        }

        return sb.ToString();
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

        if (!string.IsNullOrWhiteSpace(context.OriginalNarrative))
        {
            sb.AppendLine();
            sb.AppendLine("## 原叙事输出 (供参考)");
            sb.AppendLine(context.OriginalNarrative);
        }

        if (!string.IsNullOrWhiteSpace(context.CorrectionGuidance))
        {
            sb.AppendLine();
            sb.AppendLine("## 修正指引");
            sb.AppendLine(context.CorrectionGuidance);
        }

        return sb.ToString();
    }
}
