using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Remote.Protocol;
using Avalonia.Threading;
using DirectorPrompt.ViewModels;
using DirectorPrompt.Views;
using DirectorPrompt.Domain.Configurations;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DirectorPrompt.Services;

public sealed class LanSharingService
(
    IServiceProvider serviceProvider,
    UserSettings     userSettings
) : ILanSharingService, IAsyncDisposable
{
    private readonly SemaphoreSlim stateLock = new(1, 1);

    private BrowserRemoteTransport? transport;
    private IDisposable? remoteServer;
    private MainWindow? remoteWindow;
    private Uri? endpoint;

    public Uri? Endpoint
    {
        get => endpoint;
        private set
        {
            if (endpoint == value)
                return;

            endpoint = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsActive));
        }
    }

    public bool IsActive => Endpoint is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task ApplyAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await stateLock.WaitAsync(cancellationToken);

        try
        {
            if (enabled == IsActive)
                return;

            if (enabled)
                await StartAsync(cancellationToken);
            else
                await StopAsync();
        }
        finally
        {
            stateLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stateLock.WaitAsync().ConfigureAwait(false);

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            stateLock.Release();
            stateLock.Dispose();
        }
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var address = GetLanAddress();
        var port    = GetAvailablePort(address, userSettings.RemoteControl.Port);
        var currentTransport = new BrowserRemoteTransport(address, port);
        currentTransport.OnException += OnTransportException;

        try
        {
            await currentTransport.StartServerAsync(cancellationToken);

            if (Dispatcher.UIThread.CheckAccess())
                CreateRemoteVisual(currentTransport);
            else
                await Dispatcher.UIThread.InvokeAsync
                (
                    () => CreateRemoteVisual(currentTransport),
                    DispatcherPriority.Send,
                    cancellationToken
                );

            currentTransport.Start();
            transport = currentTransport;
            Endpoint  = new Uri($"http://{address}:{port}/");

            Log.Information("局域网共享已开启: {Endpoint}", Endpoint);
        }
        catch
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                remoteServer?.Dispose();
                remoteServer = null;
                remoteWindow = null;
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync
                (
                    () =>
                    {
                        remoteServer?.Dispose();
                        remoteServer = null;
                        remoteWindow = null;
                    }
                );
            }
            currentTransport.OnException -= OnTransportException;
            await currentTransport.DisposeAsync();
            throw;
        }
    }

    private async Task StopAsync()
    {
        Endpoint = null;

        if (Dispatcher.UIThread.CheckAccess())
        {
            remoteServer?.Dispose();
            remoteServer = null;
            remoteWindow = null;
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync
            (
                () =>
                {
                    remoteServer?.Dispose();
                    remoteServer = null;
                    remoteWindow = null;
                }
            );
        }

        if (transport is not null)
        {
            transport.OnException -= OnTransportException;
            await transport.DisposeAsync().ConfigureAwait(false);
            transport = null;
        }

        Log.Information("局域网共享已关闭");
    }

    private void CreateRemoteVisual(IAvaloniaRemoteTransportConnection currentTransport)
    {
        var viewModel = serviceProvider.GetRequiredService<MainViewModel>();
        remoteWindow  = new MainWindow(viewModel, false);

        if (remoteWindow.Content is not Control content)
            throw new InvalidOperationException("主界面内容无法用于远程控制");

        remoteWindow.Content = null;
        content.DataContext  = viewModel;

        var serverType = typeof(Control).Assembly.GetType("Avalonia.Controls.Remote.RemoteServer", true)!;
        remoteServer = (IDisposable?)Activator.CreateInstance
        (
            serverType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [currentTransport],
            null
        ) ?? throw new InvalidOperationException("Avalonia 远程控制服务不可用");

        serverType.GetProperty("Content")!.SetValue(remoteServer, content);
    }

    private static IPAddress GetLanAddress()
    {
        var address = NetworkInterface.GetAllNetworkInterfaces()
                                      .Where(static item => item.OperationalStatus == OperationalStatus.Up &&
                                                            item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                                      .OrderByDescending(static item => item.GetIPProperties().GatewayAddresses.Count > 0)
                                      .SelectMany(static item => item.GetIPProperties().UnicastAddresses)
                                      .Select(static item => item.Address)
                                      .FirstOrDefault(static item => item.AddressFamily == AddressFamily.InterNetwork &&
                                                                     !IPAddress.IsLoopback(item));

        return address ?? throw new InvalidOperationException("未找到可用的局域网 IPv4 地址");
    }

    private static int GetAvailablePort(IPAddress address, int preferredPort)
    {
        if (preferredPort is < 1024 or > 65435)
            preferredPort = 32145;

        for (var port = preferredPort; port < preferredPort + 100; port++)
        {
            var listener = new TcpListener(address, port);

            try
            {
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
            }
            finally
            {
                listener.Stop();
            }
        }

        throw new InvalidOperationException("固定端口附近没有可用端口");
    }

    private static void OnTransportException(IAvaloniaRemoteTransportConnection sender, Exception exception) =>
        Log.Warning(exception, "局域网远程连接异常");

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
