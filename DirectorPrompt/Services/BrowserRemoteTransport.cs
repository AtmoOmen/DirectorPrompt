using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using Avalonia.Remote.Protocol;
using Avalonia.Remote.Protocol.Input;
using Avalonia.Remote.Protocol.Viewport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using RemoteKey = Avalonia.Remote.Protocol.Input.Key;
using RemotePhysicalKey = Avalonia.Remote.Protocol.Input.PhysicalKey;

namespace DirectorPrompt.Services;

public sealed class BrowserRemoteTransport : IAvaloniaRemoteTransportConnection, IAsyncDisposable
{
    private readonly Lock          connectionLock = new();
    private readonly IPAddress     address;
    private readonly int           port;
    private readonly byte[]        page;
    private readonly SemaphoreSlim sendLock = new(1, 1);

    private WebApplication? application;
    private PendingFrame?   latestFrame;
    private bool            isStarted;
    private long            sentSequenceID = -1;
    private WebSocket?      socket;

    public event Action<IAvaloniaRemoteTransportConnection, object>? OnMessage;

    public event Action<IAvaloniaRemoteTransportConnection, Exception>? OnException;

    public event Action<double, double>? ViewportChanged;

    public BrowserRemoteTransport(IPAddress address, int port)
    {
        this.address = address;
        this.port    = port;

        using var stream = Assembly.GetExecutingAssembly()
                                   .GetManifestResourceStream("DirectorPrompt.Assets.Remote.index.html") ??
                           throw new InvalidOperationException("远程控制页面资源不存在");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        page = memory.ToArray();

        Log.Information("已创建浏览器远程传输: 地址={Address}, 端口={Port}, 页面字节数={PageByteCount}", address, port, page.Length);
    }

    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("开始启动浏览器远程服务: 地址={Address}, 端口={Port}", address, port);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel
        (options => { options.Listen(address, port); }
        );
        builder.Services.AddRouting();
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(2));

        application = builder.Build();
        application.UseWebSockets();
        application.MapGet("/", () => Results.Bytes(page, "text/html; charset=utf-8"));
        application.Map("/remote", HandleRemoteAsync);

        await application.StartAsync(cancellationToken);
        Log.Information("浏览器远程服务已启动: 地址={Address}, 端口={Port}", address, port);
    }

    public void Start()
    {
        isStarted = true;
        Log.Debug("浏览器远程传输已开始接收 Avalonia 渲染数据");
        StartRemoteSession();
    }

    private void StartRemoteSession()
    {
        lock (connectionLock)
        {
            if (!isStarted || socket is null)
                return;
        }

        OnMessage?.Invoke
        (
            this,
            new ClientSupportedPixelFormatsMessage { Formats = [PixelFormat.Rgba8888] }
        );
    }

    public async Task Send(object data)
    {
        if (data is not FrameMessage frame)
            return;

        var frameData    = frame.Data.ToArray();
        var encodedFrame = await Task.Run(() => EncodeFrame(frameData)).ConfigureAwait(false);
        var header = FormattableString.Invariant
        (
            $"frame:{frame.SequenceId}:{frame.Width}:{frame.Height}:{frame.Stride}:{frame.DpiX}:{frame.DpiY}:{encodedFrame.Encoding}"
        );

        lock (connectionLock)
        {
            latestFrame = new PendingFrame(frame.SequenceId, header, encodedFrame.Data);
        }

        await SendLatestFrameAsync();
    }

    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        Log.Information("开始停止浏览器远程传输: 地址={Address}, 端口={Port}", address, port);

        WebSocket? currentSocket;

        lock (connectionLock)
        {
            currentSocket = socket;
            socket        = null;
        }

        if (currentSocket is not null)
        {
            currentSocket.Abort();
            currentSocket.Dispose();
        }

        var currentApplication = application;
        application = null;

        if (currentApplication is not null)
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            try
            {
                await currentApplication.StopAsync(stopTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Warning("浏览器远程服务停止超时: 地址={Address}, 端口={Port}", address, port);
            }

            try
            {
                await currentApplication.DisposeAsync()
                                        .AsTask()
                                        .WaitAsync(TimeSpan.FromSeconds(3))
                                        .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Log.Warning("浏览器远程服务释放超时: 地址={Address}, 端口={Port}", address, port);
            }
        }

        sendLock.Dispose();
        Log.Information("浏览器远程传输已停止: 地址={Address}, 端口={Port}", address, port);
    }

    private async Task HandleRemoteAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            Log.Warning("浏览器远程服务拒绝非 WebSocket 请求: 远程地址={RemoteAddress}", context.Connection.RemoteIpAddress);
            return;
        }

        var currentSocket = await context.WebSockets.AcceptWebSocketAsync();

        Log.Information("浏览器远程客户端已连接: 远程地址={RemoteAddress}", context.Connection.RemoteIpAddress);

        lock (connectionLock)
        {
            socket?.Abort();
            socket         = currentSocket;
            sentSequenceID = -1;
        }

        StartRemoteSession();
        await SendLatestFrameAsync();

        try
        {
            var buffer = new byte[4096];

            while (currentSocket.State == WebSocketState.Open)
            {
                var result = await currentSocket.ReceiveAsync(buffer, context.RequestAborted);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await currentSocket.CloseOutputAsync
                    (
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription,
                        CancellationToken.None
                    );
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                HandleMessage(message);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Debug("浏览器远程客户端连接已取消: 远程地址={RemoteAddress}", context.Connection.RemoteIpAddress);
        }
        catch (WebSocketException ex)
        {
            Log.Warning(ex, "浏览器远程 WebSocket 异常: 远程地址={RemoteAddress}", context.Connection.RemoteIpAddress);
            OnException?.Invoke(this, ex);
        }
        finally
        {
            lock (connectionLock)
            {
                if (ReferenceEquals(socket, currentSocket))
                    socket = null;
            }

            currentSocket.Dispose();
            Log.Information("浏览器远程客户端已断开: 远程地址={RemoteAddress}", context.Connection.RemoteIpAddress);
        }
    }

    private void HandleMessage(string message)
    {
        var parts = message.Split(':');

        try
        {
            switch (parts[0])
            {
                case "frame-received":
                    OnMessage?.Invoke(this, new FrameReceivedMessage { SequenceId = long.Parse(parts[1], CultureInfo.InvariantCulture) });
                    break;
                case "resize":
                    Resize
                    (
                        ParseDouble(parts[1]),
                        ParseDouble(parts[2]),
                        parts.Length > 3 ?
                            ParseDouble(parts[3]) * 96 :
                            96
                    );
                    break;
                case "pointer-moved":
                    OnMessage?.Invoke
                    (
                        this,
                        new PointerMovedEventMessage
                        {
                            Modifiers = ParseModifiers(parts[1]),
                            X         = ParseDouble(parts[2]),
                            Y         = ParseDouble(parts[3])
                        }
                    );
                    break;
                case "pointer-pressed":
                    OnMessage?.Invoke
                    (
                        this,
                        new PointerPressedEventMessage
                        {
                            Modifiers = ParseModifiers(parts[1]),
                            X         = ParseDouble(parts[2]),
                            Y         = ParseDouble(parts[3]),
                            Button    = ParseButton(parts[4])
                        }
                    );
                    break;
                case "pointer-released":
                    OnMessage?.Invoke
                    (
                        this,
                        new PointerReleasedEventMessage
                        {
                            Modifiers = ParseModifiers(parts[1]),
                            X         = ParseDouble(parts[2]),
                            Y         = ParseDouble(parts[3]),
                            Button    = ParseButton(parts[4])
                        }
                    );
                    break;
                case "scroll":
                    OnMessage?.Invoke
                    (
                        this,
                        new ScrollEventMessage
                        {
                            Modifiers = ParseModifiers(parts[1]),
                            X         = ParseDouble(parts[2]),
                            Y         = ParseDouble(parts[3]),
                            DeltaX    = ParseDouble(parts[4]),
                            DeltaY    = ParseDouble(parts[5])
                        }
                    );
                    break;
                case "key":
                    OnMessage?.Invoke(this, CreateKeyMessage(parts));
                    break;
                case "text":
                    OnMessage?.Invoke(this, new TextInputEventMessage { Text = Uri.UnescapeDataString(parts[1]) });
                    break;
            }
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException)
        {
            Log.Warning(ex, "浏览器远程消息解析失败: 消息类型={MessageType}", parts[0]);
            OnException?.Invoke(this, ex);
        }
    }

    private void Resize(double width, double height, double dpi = 96)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return;

        OnMessage?.Invoke
        (
            this,
            new ClientViewportAllocatedMessage
            {
                Width  = width,
                Height = height,
                DpiX   = dpi,
                DpiY   = dpi
            }
        );

        ViewportChanged?.Invoke(width, height);
        Log.Debug("浏览器远程视口已更新: 宽={Width}, 高={Height}, DPI={Dpi}", width, height, dpi);
    }

    private static EncodedFrame EncodeFrame(byte[] data)
    {
        using var compressed = new MemoryStream();

        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, true))
            gzip.Write(data);

        if (compressed.Length >= data.Length)
            return new EncodedFrame(data, "raw");

        return new EncodedFrame(compressed.ToArray(), "gzip");
    }

    private static KeyEventMessage CreateKeyMessage(string[] parts)
    {
        var physicalKey = Enum.TryParse<RemotePhysicalKey>(parts[4], true, out var parsedPhysicalKey) ?
                              parsedPhysicalKey :
                              RemotePhysicalKey.None;
        var keyName = parts[3] switch
        {
            "ArrowLeft"                                                   => "Left",
            "ArrowUp"                                                     => "Up",
            "ArrowRight"                                                  => "Right",
            "ArrowDown"                                                   => "Down",
            "Backspace"                                                   => "Back",
            " "                                                           => "Space",
            _ when parts[4].StartsWith("Key",   StringComparison.Ordinal) => parts[4][3..],
            _ when parts[4].StartsWith("Digit", StringComparison.Ordinal) => $"D{parts[4][5..]}",
            _                                                             => parts[3]
        };
        var key = Enum.TryParse<RemoteKey>(keyName, true, out var parsedKey) ?
                      parsedKey :
                      RemoteKey.None;

        return new KeyEventMessage
        {
            IsDown      = parts[1] == "down",
            Modifiers   = ParseModifiers(parts[2]),
            Key         = key,
            PhysicalKey = physicalKey,
            KeySymbol   = parts[3]
        };
    }

    private static InputModifiers[]? ParseModifiers(string value) =>
        string.IsNullOrEmpty(value) ?
            null :
            value.Split(',').Select(static item => Enum.Parse<InputModifiers>(item, true)).ToArray();

    private static MouseButton ParseButton(string value) =>
        Enum.TryParse<MouseButton>(value, true, out var button) ?
            button :
            MouseButton.None;

    private static double ParseDouble(string value) =>
        double.Parse(value, CultureInfo.InvariantCulture);

    private async Task SendLatestFrameAsync()
    {
        await sendLock.WaitAsync();

        try
        {
            WebSocket?    currentSocket;
            PendingFrame? frame;

            lock (connectionLock)
            {
                currentSocket = socket;
                frame         = latestFrame;

                if (currentSocket is not { State: WebSocketState.Open } ||
                    frame is null                                       ||
                    frame.SequenceID == sentSequenceID)
                    return;
            }

            await currentSocket.SendAsync(Encoding.UTF8.GetBytes(frame.Header), WebSocketMessageType.Text,   true, CancellationToken.None);
            await currentSocket.SendAsync(frame.Data,                           WebSocketMessageType.Binary, true, CancellationToken.None);

            lock (connectionLock)
            {
                if (ReferenceEquals(socket, currentSocket))
                    sentSequenceID = frame.SequenceID;
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
        {
            Log.Warning(ex, "浏览器远程帧发送失败");
            OnException?.Invoke(this, ex);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private sealed record EncodedFrame
    (
        byte[] Data,
        string Encoding
    );

    private sealed record PendingFrame
    (
        long   SequenceID,
        string Header,
        byte[] Data
    );
}
