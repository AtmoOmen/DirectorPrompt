using Avalonia;
using Velopack;

namespace DirectorPrompt;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .With(new Win32PlatformOptions { OverlayPopups = true })
                  .With(new X11PlatformOptions { OverlayPopups = true })
                  .With(new AvaloniaNativePlatformOptions { OverlayPopups = true })
                  .LogToTrace();
}
