using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DirectorPrompt.Domain;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace DirectorPrompt.Infrastructure.AI;

internal sealed class OpenAIReasoningChatClient
(
    IChatClient inner
) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync
    (
        IEnumerable<ChatMessage> messages,
        ChatOptions?             options           = null,
        CancellationToken        cancellationToken = default
    )
    {
        var response = await InnerClient.GetResponseAsync(PrepareMessages(messages), options, cancellationToken);

        if (response.RawRepresentation is not ChatCompletion completion)
            return response;

        using var document = JsonDocument.Parse(ModelReaderWriter.Write(completion));
        var       state    = new OpenAIReasoningState();
        var       text     = state.Append(document.RootElement, false);

        foreach (var message in response.Messages)
        {
            if (text.Length > 0 && message.Contents.OfType<TextReasoningContent>().All(content => string.IsNullOrEmpty(content.Text)))
                message.Contents.Insert(0, new TextReasoningContent(text));

            state.Attach(message.Contents);
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync
    (
        IEnumerable<ChatMessage>                   messages,
        ChatOptions?                               options           = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var state = new OpenAIReasoningState();

        await foreach (var update in InnerClient.GetStreamingResponseAsync(PrepareMessages(messages), options, cancellationToken))
        {
            if (update.RawRepresentation is StreamingChatCompletionUpdate raw)
            {
                using var document = JsonDocument.Parse(ModelReaderWriter.Write(raw));
                var       text     = state.Append(document.RootElement, true);

                if (text.Length > 0 && update.Contents.OfType<TextReasoningContent>().All(content => string.IsNullOrEmpty(content.Text)))
                    update.Contents.Insert(0, new TextReasoningContent(text));
            }

            state.Attach(update.Contents);
            yield return update;
        }
    }

    private static IEnumerable<ChatMessage> PrepareMessages(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            var state = message.Contents
                               .Select(content => content.AdditionalProperties?.GetValueOrDefault(nameof(OpenAIReasoningState)))
                               .Select
                               (value => value switch
                                   {
                                       OpenAIReasoningState reasoningState => reasoningState,
                                       JsonElement element                 => element.Deserialize<OpenAIReasoningState>(JsonOptions.Compact),
                                       _                                   => null
                                   }
                               )
                               .FirstOrDefault(value => value is not null);

            if (message.Role != ChatRole.Assistant || state is null)
            {
                yield return message;
                continue;
            }

            var native = new AssistantChatMessage
            (
                string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text))
            );

            foreach (var call in message.Contents.OfType<FunctionCallContent>())
                native.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(call.CallId, call.Name, BinaryData.FromObjectAsJson(call.Arguments)));

            state.Apply(native);
            yield return new ChatMessage(message.Role, message.Contents)
            {
                AuthorName           = message.AuthorName,
                AdditionalProperties = message.AdditionalProperties,
                RawRepresentation    = native
            };
        }
    }
}
