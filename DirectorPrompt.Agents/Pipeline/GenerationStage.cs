using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DirectorPrompt.Agents.Config;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class GenerationStage
(
    IChatClientFactory  chatClientFactory,
    AgentConfigResolver agentConfigResolver,
    IAgentToolResolver  agentToolResolver,
    IEventRepository    eventRepository,
    ISceneRepository    sceneRepository
)
{
    public async Task ExecuteAsync
    (
        PipelineContext   context,
        CancellationToken cancellationToken = default
    )
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

        var client = chatClientFactory.Create(resolved.ProviderConfig, resolved.ModelConfig, false);
        var tools = await agentToolResolver.ResolveAsync
                    (
                        AgentTaskType.Narrator,
                        context.ToolContext,
                        cancellationToken
                    );
        var draft    = new NarrativeDraft(text => context.OnStreamingUpdate?.Invoke(new NarrationUpdate(Narrative: text)));
        var reader   = new StoryReader(eventRepository, sceneRepository, context);
        var allTools = draft.CreateTools().Concat(reader.CreateTools()).Concat(tools).ToList();

        if (allTools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count() != allTools.Count)
            throw new InvalidOperationException("叙事工具名称重复，请检查外部 MCP 工具配置。");

        var systemPrompt = BuildSystemPrompt(context, resolved.SystemPrompt);
        var userMessage  = BuildNarratorInput(context);

        Log.Debug("Narrator 输入长度={Length}", userMessage.Length);

        var messages = BuildMessages(systemPrompt, resolved.ModelPrompt, context);

        messages.Add(new ChatMessage(ChatRole.User, userMessage));

        var options = new ChatOptions
        {
            Temperature            = resolved.ModelConfig.Temperature,
            ModelId                = resolved.ModelConfig.ModelName,
            Tools                  = [.. allTools],
            ToolMode               = ChatToolMode.RequireAny,
            AllowMultipleToolCalls = true
        };

        var       toolLookup = allTools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        const int MAX_TURNS  = 48;

        for (var turn = 1; turn <= MAX_TURNS; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await ReadTurnAsync(client, messages, options, context, turn, cancellationToken);
            messages.AddRange(response.Messages);

            var calls = response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().ToList();

            if (calls.Count == 0)
                throw new InvalidOperationException("叙事模型未调用写作工具。请使用支持工具调用的模型；草稿尚未提交。");

            var results  = new List<AIContent>();
            var hadError = false;

            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var activity = new NarrationActivity
                (
                    context.NarrationActivities.Count,
                    turn,
                    NarrationActivityKind.Tool,
                    call.Name,
                    string.Empty,
                    PipelineStageStatus.Running,
                    JsonSerializer.Serialize(call.Arguments, JsonOptions.Default)
                );
                PublishActivity(context, activity);

                object? result;
                var     status = PipelineStageStatus.Complete;

                try
                {
                    if (!toolLookup.TryGetValue(call.Name, out var tool))
                        throw new ArgumentException($"工具不存在：{call.Name}");

                    if (call.Name == "submit_story" && (hadError || !ReferenceEquals(call, calls[^1])))
                        throw new ArgumentException("请先处理本轮工具错误，并将 submit_story 作为最后一个工具调用。");

                    result = await tool.InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Log.Warning(exception, "叙事工具调用失败: {Tool}", call.Name);
                    result   = ToolResult.Error(exception.Message);
                    status   = PipelineStageStatus.Failed;
                    hadError = true;
                }

                results.Add(new FunctionResultContent(call.CallId, result));
                PublishActivity
                (
                    context,
                    activity with
                    {
                        Content = result?.ToString() ?? string.Empty,
                        Status = status
                    }
                );
            }

            messages.Add(new ChatMessage(ChatRole.Tool, results));

            if (!draft.IsSubmitted)
                continue;

            context.NarrativeOutput  = draft.Text;
            context.NarrativeIntents = new Dictionary<string, string>(draft.Intents);
            Log.Information("GenerationStage 完成: 叙事长度={Length}, 工具轮数={Turns}", draft.Text.Length, turn);
            return;
        }

        throw new InvalidOperationException($"叙事工具调用已达到 {MAX_TURNS} 轮，草稿尚未提交。");
    }

    private static async Task<ChatResponse> ReadTurnAsync
    (
        IChatClient       client,
        List<ChatMessage> messages,
        ChatOptions       options,
        PipelineContext   context,
        int               turn,
        CancellationToken cancellationToken
    )
    {
        var                updates        = new List<ChatResponseUpdate>();
        NarrationActivity? activity       = null;
        var                contentBuilder = new StringBuilder();
        var                lastPublished  = 0L;
        var                thinkingParser = new StreamingThinkingParser();

        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            updates.Add(update);

            var contents = update.Contents.SelectMany
            (content => content is TextContent text ?
                            thinkingParser.Append(text.Text) :
                            [content]
            );

            foreach (var content in contents)
                PublishContent(content);
        }

        foreach (var content in thinkingParser.Append(string.Empty, true))
            PublishContent(content);

        if (activity is not null)
            PublishActivity(context, activity with { Content = contentBuilder.ToString(), Status = PipelineStageStatus.Complete });

        var response = updates.ToChatResponse();

        return response.FinishReason == ChatFinishReason.Length ?
                   throw new InvalidOperationException("叙事模型达到输出长度限制，本轮工具参数可能不完整，草稿未提交。") :
                   response;

        void PublishContent(AIContent content)
        {
            var kind = content switch
            {
                TextReasoningContent => NarrationActivityKind.Reasoning,
                TextContent          => NarrationActivityKind.Message,
                _                    => (NarrationActivityKind?)null
            };
            var text = content switch
            {
                TextReasoningContent reasoning => reasoning.Text,
                TextContent message            => message.Text,
                _                              => null
            };

            if (kind is null || string.IsNullOrEmpty(text))
                return;

            if (activity?.Kind != kind)
            {
                if (activity is not null)
                    PublishActivity(context, activity with { Content = contentBuilder.ToString(), Status = PipelineStageStatus.Complete });

                contentBuilder.Clear();
                lastPublished = 0L;
                activity = new NarrationActivity
                (
                    context.NarrationActivities.Count,
                    turn,
                    kind.Value,
                    kind == NarrationActivityKind.Reasoning ?
                        "思考" :
                        "创作说明",
                    string.Empty,
                    PipelineStageStatus.Running
                );
            }

            contentBuilder.Append(text);

            if (Stopwatch.GetElapsedTime(lastPublished) < TimeSpan.FromMilliseconds(50))
                return;

            activity = activity with { Content = contentBuilder.ToString() };
            PublishActivity(context, activity);
            lastPublished = Stopwatch.GetTimestamp();
        }
    }

    private static void PublishActivity
    (
        PipelineContext   context,
        NarrationActivity activity
    )
    {
        if (activity.ID == context.NarrationActivities.Count)
            context.NarrationActivities.Add(activity);
        else
            context.NarrationActivities[activity.ID] = activity;

        context.OnStreamingUpdate?.Invoke(new NarrationUpdate(Activity: activity));
    }

    private static List<ChatMessage> BuildMessages
    (
        string          systemPrompt,
        string?         modelPrompt,
        PipelineContext context
    )
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(modelPrompt))
            messages.Add(new ChatMessage(ChatRole.System, modelPrompt));

        messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

        if (!string.IsNullOrWhiteSpace(context.Project?.OpeningMessage))
        {
            messages.Add(new ChatMessage(ChatRole.User, "读取开场叙事，作为已经发生的故事背景。"));
            AddStoryRound(0, null, string.Empty, context.Project.OpeningMessage);
        }

        foreach (var entry in context.History)
        {
            messages.Add(new ChatMessage(ChatRole.User, entry.DirectorInput));
            AddStoryRound(entry.RoundID, context.CurrentSceneID, entry.DirectorInput, entry.NarrativeOutput);
        }

        return messages;

        void AddStoryRound(long roundID, long? sceneID, string directorInput, string text)
        {
            var callID = $"story_round_{roundID}";
            messages.Add
            (
                new ChatMessage
                (
                    ChatRole.Assistant,
                    [new FunctionCallContent(callID, "read_story_round", new Dictionary<string, object?> { ["roundID"] = roundID, ["lineCount"] = 0 })]
                )
            );
            messages.Add
            (
                new ChatMessage
                (
                    ChatRole.Tool,
                    [new FunctionResultContent(callID, StoryReader.FormatRound(roundID, sceneID, directorInput, text))]
                )
            );
        }
    }

    private static string BuildSystemPrompt
    (
        PipelineContext context,
        string          basePrompt
    )
    {
        var sb = new StringBuilder();

        sb.AppendLine(basePrompt);
        sb.AppendLine();
        sb.AppendLine
        (
            """
            ## 写作工具协议
            历史正文和开场叙事通过 read_story_round 工具结果提供，不作为普通回复。已提供的工具结果可直接用于承接；缺少原文时，使用 list_story_rounds 分页查找轮次，再用 read_story_round 按轮次、行范围读取。roundID=0 为开场，read_story 仍只读取本轮草稿。
            list_story_scenes 列出可读场景，list_story_rounds 默认列出当前场景，可指定其他场景并通过 nextBeforeRoundID 继续向前翻页。read_story_round 返回 totalLines 和 nextStartLine，可逐段读取长轮次，lineCount=0 可读取至文末。读取历史不会把旧正文写入本轮草稿。
            本轮从 revision=0 的空草稿开始，只存放当前导演指令对应的新增正文，不是包含历史章节的整篇故事文档。只通过 append_story、write_story、edit_story 编写正文。
            历史消息、开场叙事和上一场景摘要是已经发生的背景，已经向读者呈现，不需要写入、恢复或合并到本轮草稿。空草稿不表示历史故事缺失。
            从历史结尾继续创作新的动作、对话和描写，不复制上一轮正文，不用回顾、概述或改写旧段落来充当新内容。仅在当前导演明确要求重写或回顾时按该要求处理。
            append_story 只向本轮草稿追加新段落；write_story 只重写本轮草稿；read_story 和 count_story 只查看、统计本轮草稿。所有工具的“全文”“开头”“完整正文”均指本轮输出，不包含此前轮次。
            必须通过多轮工具调用逐步创作，不得在一次思考中预写故事，再一次性写入并提交。
            创作前只确定本次要写的局部目标、场景边界和必要信息，然后立即调用工具。具体叙事措辞直接进入草稿，不在思考中试写段落、对白或备选版本。

            按以下顺序推进，每一步都必须等待相关工具结果后再决定下一步：
            1. 首次使用 append_story 写入本轮新增正文的第一个局部叙事单元，直接承接历史结尾；不要复制历史开头或上一轮正文，不要在首次调用中写完本轮全部正文，也不要预先排好本轮所有写入调用。
            2. 后续调用轮使用 read_story 查看已写草稿，依据实际文本决定继续 append_story 或通过 edit_story 精修。需要全面重构时才使用 write_story。
            3. 每次只推进当前需要处理的内容，检查前后衔接、人物行为、叙事节奏、重复、设定和导演约束；发现问题就实际修改草稿，不只在说明中声称已修正。
            4. 最后一次正文修改后，在后续调用轮重新 read_story 复核，检查是否混入历史段落；若有，先删除或改为真正的新叙事。有字数要求时使用 count_story 核对，字数要求针对本轮新增正文，不得靠复制历史凑足。复核中若再次修改正文，必须重新读取修改后的版本。
            5. 收到最终复核结果并确认内容完成后，再在新的调用轮单独调用 submit_story。不得把写入与提交、读取与依赖其结果的修改或提交预先放在同一调用轮。

            多轮调用用于实际撰写与检查，不得为了增加轮数重复读取未变化的内容、进行无意义修改或无必要的检索。很短的故事可以一次写完草稿，但仍需在后续调用轮读取复核，再单独提交。
            工具返回的 revision 是下一次修改所需的 expectedRevision，不得预估后续版本号并提前生成依赖调用。
            set_narrative_intent 记录已在正文落实的意图，可反复更新或移除；意图不能代替正文事实，也不能授权提前兑现伏笔。
            所有普通回复与思考仅用于创作过程，不会进入故事。正文只通过写作工具写入，通过读取工具查看，不在普通回复中复述。
            提交是最后一次调用，不需要再次输出全文。
            """
        );

        if (context.Project is not null)
        {
            if (!string.IsNullOrWhiteSpace(context.Project.Description))
            {
                sb.AppendLine();
                sb.AppendLine("## 项目设定");
                sb.AppendLine(context.Project.Description);
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

    private static string BuildNarratorInput
    (
        PipelineContext context
    )
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
