using Microsoft.Extensions.AI;

namespace DirectorPrompt.Agents;

public sealed class StreamingThinkingParser
{
    private static readonly string[] OpeningTags = ["<think>", "<thinking>", "<analysis>"];

    private string  pending = string.Empty;
    private string? closingTag;

    public IEnumerable<AIContent> Append(string text, bool complete = false)
    {
        pending += text;

        while (pending.Length > 0)
        {
            var tags = closingTag is null ?
                           OpeningTags :
                           [closingTag];
            var     tagIndex   = -1;
            string? matchedTag = null;

            foreach (var tag in tags)
            {
                var index = pending.IndexOf(tag, StringComparison.OrdinalIgnoreCase);

                if (index < 0 || (tagIndex >= 0 && index >= tagIndex))
                    continue;

                tagIndex   = index;
                matchedTag = tag;
            }

            if (matchedTag is not null)
            {
                if (tagIndex > 0)
                    yield return CreateContent(pending[..tagIndex]);

                pending = pending[(tagIndex + matchedTag.Length)..];
                closingTag = closingTag is null ?
                                 matchedTag.Insert(1, "/") :
                                 null;
                continue;
            }

            var retained = 0;

            if (!complete)
            {
                foreach (var tag in tags)
                {
                    var length = Math.Min(pending.Length, tag.Length - 1);

                    for (; length > retained; length--)
                    {
                        if (!pending.EndsWith(tag[..length], StringComparison.OrdinalIgnoreCase))
                            continue;

                        retained = length;
                        break;
                    }
                }
            }

            var available = pending.Length - retained;

            if (available > 0)
                yield return CreateContent(pending[..available]);

            pending = pending[available..];
            break;
        }
    }

    private AIContent CreateContent(string text) =>
        closingTag is null ?
            new TextContent(text) :
            new TextReasoningContent(text);
}
