using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.IO.Compression;
using Avalonia.Remote.Protocol.Viewport;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Services;

namespace DirectorPrompt.Tests;

public sealed class BrowserRemoteTransportTests
{
    [Fact]
    public void RemoteControlConfigUsesStableDefaultPort()
    {
        var config = new RemoteControlConfig();

        Assert.Equal(32145, config.Port);
    }

    [Fact]
    public async Task ServerServesRemotePageAndReleasesPort()
    {
        var port      = GetAvailablePort();
        var transport = new BrowserRemoteTransport(IPAddress.Loopback, port);

        await transport.StartServerAsync();

        using var client = new HttpClient();
        var page = await client.GetStringAsync($"http://127.0.0.1:{port}/");

        Assert.Contains("<canvas id=\"screen\"", page);
        Assert.Contains("id=\"keyboardButton\"", page);
        Assert.Contains("Math.max(360,innerWidth)", page);
        Assert.Contains("devicePixelRatio", page);
        Assert.Contains("DecompressionStream", page);
        Assert.Contains("logicalWidth", page);
        Assert.Contains("currentFrame?.dpiX", page);

        await transport.DisposeAsync();

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
    }

    [Fact]
    public async Task FrameProducedBeforeConnectionIsDelivered()
    {
        var port      = GetAvailablePort();
        var transport = new BrowserRemoteTransport(IPAddress.Loopback, port);
        await transport.StartServerAsync();
        await transport.Send(new FrameMessage
        {
            SequenceId = 7,
            Width      = 1,
            Height     = 1,
            Stride     = 4,
            DpiX       = 96,
            DpiY       = 96,
            Format     = PixelFormat.Rgba8888,
            Data       = [1, 2, 3, 4]
        });

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/remote"), CancellationToken.None);

        var buffer       = new byte[256];
        var headerResult = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var header       = Encoding.UTF8.GetString(buffer, 0, headerResult.Count);
        var frameResult  = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.StartsWith("frame:7:1:1:4", header);
        Assert.Equal(WebSocketMessageType.Binary, frameResult.MessageType);
        Assert.Equal([1, 2, 3, 4], buffer[..frameResult.Count]);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task RepetitiveFrameIsDeliveredAsLosslessGzip()
    {
        var port      = GetAvailablePort();
        var transport = new BrowserRemoteTransport(IPAddress.Loopback, port);
        var data      = Enumerable.Repeat((byte)42, 32 * 32 * 4).ToArray();

        await transport.StartServerAsync();
        await transport.Send(new FrameMessage
        {
            SequenceId = 8,
            Width      = 32,
            Height     = 32,
            Stride     = 32 * 4,
            DpiX       = 192,
            DpiY       = 192,
            Format     = PixelFormat.Rgba8888,
            Data       = data
        });

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/remote"), CancellationToken.None);

        var headerBuffer = new byte[256];
        var headerResult = await socket.ReceiveAsync(headerBuffer, CancellationToken.None);
        var header       = Encoding.UTF8.GetString(headerBuffer, 0, headerResult.Count);
        var frameBuffer  = new byte[4096];
        var frameResult  = await socket.ReceiveAsync(frameBuffer, CancellationToken.None);

        Assert.EndsWith(":gzip", header);
        Assert.Equal(WebSocketMessageType.Binary, frameResult.MessageType);

        using var compressed = new MemoryStream(frameBuffer[..frameResult.Count].ToArray());
        using var gzip       = new GZipStream(compressed, CompressionMode.Decompress);
        using var restored   = new MemoryStream();
        gzip.CopyTo(restored);

        Assert.Equal(data, restored.ToArray());

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task DisposeCompletesWithConnectedBrowser()
    {
        var port      = GetAvailablePort();
        var transport = new BrowserRemoteTransport(IPAddress.Loopback, port);
        await transport.StartServerAsync();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/remote"), CancellationToken.None);

        await transport.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
