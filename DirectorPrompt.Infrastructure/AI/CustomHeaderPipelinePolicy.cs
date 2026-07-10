using System.ClientModel.Primitives;
using System.Text.Json;
using Serilog;

namespace DirectorPrompt.Infrastructure.AI;

internal sealed class CustomHeaderPipelinePolicy : PipelinePolicy
{
    private readonly Dictionary<string, string> headers;

    private CustomHeaderPipelinePolicy(Dictionary<string, string> headers) =>
        this.headers = headers;

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ApplyHeaders(message);

        if (currentIndex + 1 < pipeline.Count)
            pipeline[currentIndex + 1].Process(message, pipeline, currentIndex + 1);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ApplyHeaders(message);

        if (currentIndex + 1 < pipeline.Count)
            await pipeline[currentIndex + 1].ProcessAsync(message, pipeline, currentIndex + 1);
    }

    private void ApplyHeaders(PipelineMessage message)
    {
        foreach (var (key, value) in headers)
            message.Request.Headers.Add(key, value);
    }

    public static Dictionary<string, string>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            return dict is { Count: > 0 } ?
                       dict :
                       null;
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "解析自定义请求头失败: {Json}", json);

            return null;
        }
    }

    public static void ApplyToOptions(ClientPipelineOptions options, string? customHeaders)
    {
        var parsed = Parse(customHeaders);

        if (parsed is null)
            return;

        options.AddPolicy(new CustomHeaderPipelinePolicy(parsed), PipelinePosition.BeforeTransport);
    }
}
