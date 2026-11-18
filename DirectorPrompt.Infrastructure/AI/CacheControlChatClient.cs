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
            {
                content.WithCacheControl((CacheControlEphemeral?)null);
            }
        }

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role != ChatRole.System)
            {
                continue;
            }

            MarkLastContent(messages[i]);
            break;
        }

        var roundContextIndex = messages.FindIndex
        (
            message => message.Role == ChatRole.Tool
                       &&
                       message.Contents.OfType<FunctionResultContent>().Any
                       (
                           content => content.CallId.StartsWith("narrative_context_", StringComparison.Ordinal)
                       )
        );

        if (roundContextIndex < 0)
        {
            if (messages.Count > 0
                &&
                messages[^1].Role != ChatRole.System)
            {
                MarkLastContent(messages[^1]);
            }

            return;
        }

        var persistentTools = new List<ChatMessage>();
        var workingTools    = new List<ChatMessage>();

        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].Role != ChatRole.Tool)
            {
                continue;
            }

            if (i < roundContextIndex)
            {
                persistentTools.Add(messages[i]);
            }
            else
            {
                workingTools.Add(messages[i]);
            }
        }

        if (workingTools.Count == 1)
        {
            if (persistentTools.Count >= 2)
            {
                MarkLastContent(persistentTools[^2]);
            }

            if (persistentTools.Count >= 1)
            {
                MarkLastContent(persistentTools[^1]);
            }

            MarkLastContent(workingTools[^1]);

            return;
        }

        if (persistentTools.Count >= 1)
        {
            MarkLastContent(persistentTools[^1]);
        }

        if (workingTools.Count >= 2)
        {
            MarkLastContent(workingTools[^2]);
        }

        if (workingTools.Count >= 1)
        {
            MarkLastContent(workingTools[^1]);
        }
    }

    private static void MarkLastContent(ChatMessage message)
    {
        if (message.Contents.Count == 0)
            return;

        message.Contents[^1].WithCacheControl(new CacheControlEphemeral());
    }
}
