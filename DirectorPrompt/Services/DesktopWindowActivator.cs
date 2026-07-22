using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace DirectorPrompt.Services;

internal static class DesktopWindowActivator
{
    public static bool IsAnyWindowActive() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
        desktop.Windows.Any(window => window.IsActive);

    public static Window? ActivateMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var window = desktop.MainWindow;

        if (window is null)
            return null;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Show();
        window.Activate();
        return window;
    }
}
