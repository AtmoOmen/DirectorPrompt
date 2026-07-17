using DirectorPrompt.Domain.Models;
using DirectorPrompt.Services;

namespace DirectorPrompt.Tests;

public sealed class RemoteInteractionRouterTests
{
    [Fact]
    public void ActiveRemoteInteractionIsConsumedOnlyOnce()
    {
        var router = new RemoteInteractionRouter();
        var service = new WindowServiceStub();

        router.Attach(service);
        router.Activate();

        Assert.Same(service, router.Consume());
        Assert.Null(router.Consume());
    }

    [Fact]
    public void DetachedRemoteServiceCannotBeConsumed()
    {
        var router = new RemoteInteractionRouter();
        var service = new WindowServiceStub();

        router.Attach(service);
        router.Activate();
        router.Detach(service);

        Assert.Null(router.Consume());
    }

    private sealed class WindowServiceStub : IWindowService
    {
        public Task<string?> InputAsync(string title, string prompt, string defaultValue) =>
            Task.FromResult<string?>(null);

        public Task<bool> EditProjectAsync(Project project) =>
            Task.FromResult(false);

        public Task ShowSettingsAsync() =>
            Task.CompletedTask;
    }
}
