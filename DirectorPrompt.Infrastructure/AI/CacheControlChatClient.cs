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

    private static void ApplyCacheBreakpoints(List<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
                content.WithCacheControl((CacheControlEphemeral?)null);
        }

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role != ChatRole.System)
                continue;

            MarkLastContent(messages[i]);
            break;
        }

        if (messages.Count > 0 && messages[^1].Role != ChatRole.System)
            MarkLastContent(messages[^1]);
    }

    private static void MarkLastContent(ChatMessage message)
    {
        if (message.Contents.Count == 0)
            return;

        message.Contents[^1].WithCacheControl(new CacheControlEphemeral());
    }
}
