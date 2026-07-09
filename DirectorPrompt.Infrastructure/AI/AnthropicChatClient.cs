using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DirectorPrompt.Infrastructure.AI;

public sealed class AnthropicChatClient : IChatClient
{
    private const string ANTHROPIC_VERSION  = "2023-06-01";
    private const string DEFAULT_BASE_URL   = "https://api.anthropic.com";
    private const int    DEFAULT_MAX_TOKENS = 8192;

    private readonly HttpClient client;
    private readonly bool       ownsClient;
    private readonly string     modelID;

    public AnthropicChatClient(string apiKey, string modelID, string? endpoint = null, HttpClient? httpClient = null)
    {
        this.modelID = modelID;
        ownsClient   = httpClient is null;
        client       = httpClient ?? new HttpClient();

        var baseURL = string.IsNullOrWhiteSpace(endpoint) ?
                          DEFAULT_BASE_URL :
                          endpoint.TrimEnd('/');
        client.BaseAddress = new Uri(baseURL);

        client.DefaultRequestHeaders.Add("x-api-key",         apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", ANTHROPIC_VERSION);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ChatResponse> GetResponseAsync
    (
        IEnumerable<ChatMessage> messages,
        ChatOptions?             options           = null,
        CancellationToken        cancellationToken = default
    )
    {
        var requestBody = BuildRequestBody(messages, options, false);
        var jsonContent = JsonSerializer.Serialize(requestBody);

        using var content  = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/v1/messages", content, cancellationToken);

        var responseJSON = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Anthropic API 错误 ({(int)response.StatusCode}): {responseJSON}");

        var doc = JsonDocument.Parse(responseJSON);

        return ParseResponse(doc.RootElement);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync
    (
        IEnumerable<ChatMessage>                   messages,
        ChatOptions?                               options           = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var requestBody = BuildRequestBody(messages, options, true);
        var jsonContent = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync
                             (
                                 request,
                                 HttpCompletionOption.ResponseHeadersRead,
                                 cancellationToken
                             );

        if (!response.IsSuccessStatusCode)
        {
            var errorJSON = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Anthropic API 错误 ({(int)response.StatusCode}): {errorJSON}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? currentBlockType  = null;
        string? currentToolCallID = null;
        string? currentToolName   = null;
        var     toolInputBuilder  = new StringBuilder();

        string? responseID      = null;
        string? modelIDFromResp = null;
        string? stopReason      = null;

        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrEmpty(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line["data: ".Length..];
            var node = JsonNode.Parse(data);

            if (node is null)
                continue;

            var type = node["type"]?.GetValue<string>();

            switch (type)
            {
                case "message_start":
                    modelIDFromResp = node["message"]?["model"]?.GetValue<string>();
                    responseID      = node["message"]?["id"]?.GetValue<string>();
                    break;

                case "content_block_start":
                    var block = node["content_block"];
                    currentBlockType = block?["type"]?.GetValue<string>();

                    if (currentBlockType == "tool_use")
                    {
                        currentToolCallID = block?["id"]?.GetValue<string>();
                        currentToolName   = block?["name"]?.GetValue<string>();
                        toolInputBuilder.Clear();
                    }

                    if (currentBlockType == "thinking")
                    {
                        yield return new ChatResponseUpdate
                        {
                            Role     = ChatRole.Assistant,
                            Contents = [new TextReasoningContent(string.Empty)]
                        };
                    }
                    else if (currentBlockType == "text")
                    {
                        yield return new ChatResponseUpdate
                        {
                            Role     = ChatRole.Assistant,
                            Contents = [new TextContent(string.Empty)]
                        };
                    }

                    break;

                case "content_block_delta":
                    var delta     = node["delta"];
                    var deltaType = delta?["type"]?.GetValue<string>();

                    if (deltaType == "text_delta")
                    {
                        var text = delta?["text"]?.GetValue<string>() ?? string.Empty;
                        yield return new ChatResponseUpdate
                        {
                            Role     = ChatRole.Assistant,
                            Contents = [new TextContent(text)]
                        };
                    }
                    else if (deltaType == "thinking_delta")
                    {
                        var text = delta?["thinking"]?.GetValue<string>() ?? string.Empty;
                        yield return new ChatResponseUpdate
                        {
                            Role     = ChatRole.Assistant,
                            Contents = [new TextReasoningContent(text)]
                        };
                    }
                    else if (deltaType == "input_json_delta")
                    {
                        var partial = delta?["partial_json"]?.GetValue<string>();
                        if (partial is not null)
                            toolInputBuilder.Append(partial);
                    }

                    break;

                case "content_block_stop":
                    if (currentBlockType == "tool_use" && currentToolName is not null)
                    {
                        var arguments = ParseToolArguments(toolInputBuilder.ToString());

                        yield return new ChatResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents =
                            [
                                new FunctionCallContent
                                (
                                    currentToolCallID ?? string.Empty,
                                    currentToolName,
                                    arguments
                                )
                            ]
                        };
                    }

                    currentBlockType  = null;
                    currentToolCallID = null;
                    currentToolName   = null;
                    toolInputBuilder.Clear();
                    break;

                case "message_delta":
                    stopReason = node["delta"]?["stop_reason"]?.GetValue<string>();
                    break;

                case "message_stop":
                    break;
            }
        }

        if (modelIDFromResp is not null || stopReason is not null)
        {
            yield return new ChatResponseUpdate
            {
                Role         = ChatRole.Assistant,
                ModelId      = modelIDFromResp,
                ResponseId   = responseID,
                FinishReason = MapStopReason(stopReason)
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(AnthropicChatClient) ?
            this :
            null;

    public void Dispose()
    {
        if (ownsClient)
            client.Dispose();
    }

    private static Dictionary<string, object?> ParseToolArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var       result = new Dictionary<string, object?>();
            using var doc    = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True   => true,
                        JsonValueKind.False  => false,
                        JsonValueKind.Null   => null,
                        _                    => prop.Value.GetRawText()
                    };
                }
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private Dictionary<string, object?> BuildRequestBody
    (
        IEnumerable<ChatMessage> messages,
        ChatOptions?             options,
        bool                     stream
    )
    {
        var messageList = messages.ToList();
        var systemParts = new List<string>();
        var apiMessages = new List<object>();

        foreach (var msg in messageList)
        {
            if (msg.Role == ChatRole.System)
            {
                if (!string.IsNullOrEmpty(msg.Text))
                    systemParts.Add(msg.Text);
                continue;
            }

            var role = msg.Role == ChatRole.Assistant ?
                           "assistant" :
                           "user";
            var contentBlocks = new List<object>();

            foreach (var c in msg.Contents)
            {
                switch (c)
                {
                    case TextContent text:
                        contentBlocks.Add(new { type = "text", text = text.Text });
                        break;

                    case TextReasoningContent:
                        break;

                    case FunctionCallContent fc:
                        contentBlocks.Add
                        (
                            new
                            {
                                type  = "tool_use",
                                id    = fc.CallId,
                                name  = fc.Name,
                                input = fc.Arguments
                            }
                        );
                        break;

                    case FunctionResultContent fr:
                        var resultText = fr.Result?.ToString() ?? string.Empty;
                        contentBlocks.Add
                        (
                            new
                            {
                                type        = "tool_result",
                                tool_use_id = fr.CallId,
                                content     = resultText
                            }
                        );
                        break;
                }
            }

            if (contentBlocks.Count > 0)
                apiMessages.Add(new { role, content = contentBlocks });
        }

        var body = new Dictionary<string, object?>
        {
            ["model"]      = options?.ModelId         ?? modelID,
            ["max_tokens"] = options?.MaxOutputTokens ?? DEFAULT_MAX_TOKENS,
            ["messages"]   = apiMessages,
            ["stream"]     = stream
        };

        if (systemParts.Count > 0)
            body["system"] = string.Join("\n\n", systemParts);

        if (options is not null)
        {
            if (options.Temperature.HasValue)
                body["temperature"] = options.Temperature.Value;

            if (options.Tools is { Count: > 0 })
                body["tools"] = options.Tools.Select(ConvertTool).ToList();

            if (options.AdditionalProperties is { Count: > 0 })
            {
                if (options.AdditionalProperties.TryGetValue("reasoning_effort", out var effort) && effort is not null)
                {
                    var effortStr = effort.ToString();
                    var budgetTokens = effortStr?.ToLowerInvariant() switch
                    {
                        "high"   => 16000,
                        "medium" => 8000,
                        "low"    => 4000,
                        _        => 4000
                    };

                    body["thinking"] = new { type = "enabled", budget_tokens = budgetTokens };
                }

                foreach (var (key, value) in options.AdditionalProperties)
                {
                    if (key != "reasoning_effort")
                        body[key] = value;
                }
            }
        }

        return body;
    }

    private static object ConvertTool(AITool tool)
    {
        if (tool is AIFunction fn)
        {
            var schema = fn.JsonSchema.ValueKind == JsonValueKind.Undefined ?
                             "{}" :
                             fn.JsonSchema.ToString();

            return new
            {
                name         = fn.Name,
                description  = fn.Description ?? string.Empty,
                input_schema = JsonSerializer.Deserialize<JsonElement>(schema)
            };
        }

        return new
        {
            name         = tool.Name,
            description  = string.Empty,
            input_schema = new { type = "object", properties = new { } }
        };
    }

    private static ChatResponse ParseResponse(JsonElement root)
    {
        var contents    = new List<AIContent>();
        var respModelID = root.GetProperty("model").GetString() ?? string.Empty;
        var respID      = root.GetProperty("id").GetString()    ?? string.Empty;
        var stopReason = root.TryGetProperty("stop_reason", out var sr) ?
                             sr.GetString() :
                             null;

        if (root.TryGetProperty("content", out var contentArray) && contentArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in contentArray.EnumerateArray())
            {
                var blockType = block.GetProperty("type").GetString();

                switch (blockType)
                {
                    case "text":
                        contents.Add(new TextContent(block.GetProperty("text").GetString() ?? string.Empty));
                        break;

                    case "thinking":
                        contents.Add(new TextReasoningContent(block.GetProperty("thinking").GetString() ?? string.Empty));
                        break;

                    case "tool_use":
                        var toolID   = block.GetProperty("id").GetString()   ?? string.Empty;
                        var toolName = block.GetProperty("name").GetString() ?? string.Empty;
                        var toolArgs = block.TryGetProperty("input", out var input) ?
                                           ParseToolArguments(input.GetRawText()) :
                                           new Dictionary<string, object?>();

                        contents.Add(new FunctionCallContent(toolID, toolName, toolArgs));
                        break;
                }
            }
        }

        var assistantMessage = new ChatMessage(ChatRole.Assistant, contents);
        var response         = new ChatResponse { ModelId = respModelID, ResponseId = respID, FinishReason = MapStopReason(stopReason) };
        response.Messages.Add(assistantMessage);

        return response;
    }

    private static ChatFinishReason? MapStopReason(string? stopReason) => stopReason switch
    {
        "end_turn"      => ChatFinishReason.Stop,
        "tool_use"      => ChatFinishReason.ToolCalls,
        "max_tokens"    => ChatFinishReason.Length,
        "stop_sequence" => ChatFinishReason.Stop,
        _               => null
    };
}
