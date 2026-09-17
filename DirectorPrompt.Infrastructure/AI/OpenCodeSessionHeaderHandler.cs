namespace DirectorPrompt.Infrastructure.AI;

internal sealed class OpenCodeSessionHeaderHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = OpenCodeSessionHeaders.Resolve();

        if (token is not null)
        {
            foreach (var name in OpenCodeSessionHeaders.Names)
                request.Headers.TryAddWithoutValidation(name, token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
