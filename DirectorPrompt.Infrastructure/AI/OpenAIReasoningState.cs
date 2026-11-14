using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace DirectorPrompt.Infrastructure.AI;

internal sealed class OpenAIReasoningState
{
    public JsonObject Reasoning { get; init; } = [];

    public Dictionary<int, JsonObject> ToolExtras { get; init; } = [];

    public string Append(JsonElement response, bool streaming)
    {
        if (!response.TryGetProperty("choices", out var choices) ||
            choices.GetArrayLength() == 0                        ||
            !choices[0].TryGetProperty
            (
                streaming ?
                    "delta" :
                    "message",
                out var message
            ))
            return string.Empty;

        var text = string.Empty;

        foreach (var name in new[] { "reasoning_content", "reasoning", "thinking", "reasoning_details" })
        {
            if (!message.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            Merge(Reasoning, name, JsonNode.Parse(value.GetRawText()));

            if (text.Length == 0)
                text = ReadText(value);
        }

        if (message.TryGetProperty("tool_calls", out var calls))
        {
            var ordinal = 0;

            foreach (var call in calls.EnumerateArray())
            {
                var index = call.TryGetProperty("index", out var callIndex) ?
                                callIndex.GetInt32() :
                                ordinal;
                ordinal++;

                if (!call.TryGetProperty("extra_content", out var extra))
                    continue;

                if (!ToolExtras.TryGetValue(index, out var stored))
                {
                    stored = [];
                    ToolExtras.Add(index, stored);
                }

                Merge(stored, "extra_content", JsonNode.Parse(extra.GetRawText()));
            }
        }

        return text;
    }

    public void Attach(IList<AIContent> contents)
    {
        if (Reasoning.Count == 0 && ToolExtras.Count == 0)
            return;

        foreach (var content in contents)
        {
            content.AdditionalProperties                               ??= [];
            content.AdditionalProperties[nameof(OpenAIReasoningState)] =   this;
        }
    }

    public void Apply(AssistantChatMessage message)
    {
#pragma warning disable SCME0001
        foreach (var (name, value) in Reasoning)
            message.Patch.Set(Encoding.UTF8.GetBytes("$." + name), BinaryData.FromString(value!.ToJsonString()));

        foreach (var (index, extra) in ToolExtras)
        {
            if (index >= message.ToolCalls.Count)
                continue;

            message.ToolCalls[index].Patch.Set("$.extra_content"u8, BinaryData.FromString(extra["extra_content"]!.ToJsonString()));
        }
#pragma warning restore SCME0001
    }

    private static string ReadText(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString() ?? string.Empty;
            case JsonValueKind.Array:
                return string.Concat(value.EnumerateArray().Select(ReadText));
            case JsonValueKind.Object:
                break;
            default:
                return string.Empty;
        }

        if (value.TryGetProperty("type", out var type) && type.GetString() is "reasoning.encrypted" or "redacted_thinking")
            return string.Empty;

        foreach (var name in new[] { "text", "summary", "thinking" })
        {
            if (value.TryGetProperty(name, out var text))
                return ReadText(text);
        }

        return string.Empty;
    }

    private static void Merge(JsonObject target, string name, JsonNode? value)
    {
        switch (target[name], value)
        {
            case (JsonObject existingObject, JsonObject incomingObject):
            {
                foreach (var (key, child) in incomingObject)
                    Merge(existingObject, key, child?.DeepClone());

                break;
            }
            case (JsonArray existingArray, JsonArray incomingArray):
            {
                foreach (var item in incomingArray)
                {
                    var index = item is JsonObject indexed ?
                                    indexed["index"]?.GetValue<int>() :
                                    null;
                    var existing = index is null ?
                                       null :
                                       existingArray.OfType<JsonObject>().FirstOrDefault(entry => entry["index"]?.GetValue<int>() == index);

                    if (existing is not null && item is JsonObject incoming)
                    {
                        foreach (var (key, child) in incoming)
                            Merge(existing, key, child?.DeepClone());
                    }
                    else
                    {
                        existingArray.Add(item?.DeepClone());
                    }
                }

                break;
            }
            case (JsonValue existingValue, JsonValue incomingValue) when
                existingValue.TryGetValue<string>
                    (out var previous)                          &&
                incomingValue.TryGetValue<string>(out var next) &&
                name is "reasoning_content" or "reasoning" or "thinking" or "text" or "summary" or "data" or "signature" or "thought_signature":
                target[name] = previous + next;
                break;
            default:
                target[name] = value;
                break;
        }
    }
}
