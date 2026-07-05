using Microsoft.Extensions.AI;
using Polly;
using Polly.Retry;

namespace DirectorPrompt.Infrastructure.AI;

internal static class ResilienceExtensions
{
    private const           int      MaxRetryAttempts = 3;
    private static readonly TimeSpan BaseDelay        = TimeSpan.FromSeconds(2);

    internal static IChatClient WithRetryPipeline(this IChatClient client)
    {
        var pipeline = new ResiliencePipelineBuilder<ChatResponse>()
                       .AddRetry
                       (
                           new RetryStrategyOptions<ChatResponse>
                           {
                               MaxRetryAttempts = MaxRetryAttempts,
                               BackoffType      = DelayBackoffType.Exponential,
                               Delay            = BaseDelay,
                               ShouldHandle = new PredicateBuilder<ChatResponse>()
                                              .Handle<HttpRequestException>()
                                              .Handle<TaskCanceledException>()
                           }
                       )
                       .Build();

        return new RetryChatClient(client, pipeline);
    }

    private sealed class RetryChatClient : DelegatingChatClient
    {
        private readonly ResiliencePipeline<ChatResponse> pipeline;

        internal RetryChatClient(IChatClient innerClient, ResiliencePipeline<ChatResponse> pipeline)
            : base(innerClient) =>
            this.pipeline = pipeline;

        public override async Task<ChatResponse> GetResponseAsync
        (
            IEnumerable<ChatMessage> messages,
            ChatOptions?             options           = null,
            CancellationToken        cancellationToken = default
        )
        {
            var response = await pipeline.ExecuteAsync
                           (
                               async token => await InnerClient.GetResponseAsync(messages, options, token),
                               cancellationToken
                           );

            return response;
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync
        (
            IEnumerable<ChatMessage> messages,
            ChatOptions?             options           = null,
            CancellationToken        cancellationToken = default
        ) =>
            InnerClient.GetStreamingResponseAsync(messages, options, cancellationToken);
    }
}
