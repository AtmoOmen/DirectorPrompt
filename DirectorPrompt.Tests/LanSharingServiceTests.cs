using System.Net;
using Avalonia.Headless.XUnit;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Tests;

public sealed class LanSharingServiceTests
{
    [AvaloniaFact]
    public async Task RemoteVisualUsesApplicationMainViewModel()
    {
        var viewModel = new MainViewModel
        (
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new LanSharingStub()
        );
        await using var transport = new BrowserRemoteTransport(IPAddress.Loopback, 32145);
        await using var service = new LanSharingService
        (
            new ServiceProviderStub(viewModel),
            new UserSettings(),
            new RemoteInteractionRouter()
        );

        var remoteWindow = service.CreateRemoteVisual(transport);

        Assert.Same(viewModel, remoteWindow.DataContext);
    }

    private sealed class ServiceProviderStub(MainViewModel viewModel) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(MainViewModel) ?
                viewModel :
                null;
    }

    private sealed class LanSharingStub : ILanSharingService
    {
        public Uri? Endpoint => null;

        public bool IsActive => false;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public Task ApplyAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
