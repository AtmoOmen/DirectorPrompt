using System.ClientModel.Primitives;

namespace DirectorPrompt.Infrastructure.AI;

internal sealed class OpenCodeSessionHeaderPolicy : PipelinePolicy
{
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

    private static void ApplyHeaders(PipelineMessage message)
    {
        var token = OpenCodeSessionHeaders.Resolve();

        if (token is null)
            return;

        foreach (var name in OpenCodeSessionHeaders.Names)
            message.Request.Headers.Set(name, token);
    }
}
