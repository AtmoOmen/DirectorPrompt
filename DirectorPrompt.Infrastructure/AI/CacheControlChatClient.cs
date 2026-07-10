using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using CacheControlEphemeral = Anthropic.Models.Messages.CacheControlEphemeral;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class CacheControlChatClient
(
    IChatClient inner
) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync
    (
        IEnumerable<ChatMessage> messages,
        ChatOptions?             options           = null,
        CancellationToken        cancellationToken = default
    )
    {
        var list = messages.ToList();

        ApplyCacheBreakpoints(list);

        return await inner.GetResponseAsync(list, options, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync
    (
        IEnumerable<ChatMessage>                   messages,
        ChatOptions?                               options,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var list = messages.ToList();

        ApplyCacheBreakpoints(list);

        await foreach (var update in inner.GetStreamingResponseAsync(list, options, cancellationToken))
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        inner.GetService(serviceType, serviceKey);

    public void Dispose() =>
        inner.Dispose();

    private static void ApplyCacheBreakpoints(IList<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role != ChatRole.System)
                continue;

            MarkLastContent(messages[i]);
            break;
        }

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role != ChatRole.Assistant)
                continue;

            MarkLastContent(messages[i]);
            break;
        }
    }

    private static void MarkLastContent(ChatMessage message)
    {
        if (message.Contents.Count == 0)
            return;

        message.Contents[^1].WithCacheControl(new CacheControlEphemeral());
    }
}
