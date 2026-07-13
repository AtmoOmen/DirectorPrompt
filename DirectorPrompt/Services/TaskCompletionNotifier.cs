using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DirectorPrompt.Services;

public sealed class TaskCompletionNotifier : IDisposable
{
    private readonly Icon             applicationIcon;
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly DispatcherTimer  hideTimer;

    private bool disposed;

    public TaskCompletionNotifier()
    {
        applicationIcon = GetApplicationIcon();
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = applicationIcon,
            Text = "DirectorPrompt"
        };

        notifyIcon.BalloonTipClicked += OnNotificationClicked;

        hideTimer       = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        hideTimer.Tick += OnHideTimerTick;
    }

    public void NotifyIfApplicationInBackground(string title, string message)
    {
        if (disposed || Application.Current.Windows.Cast<Window>().Any(window => window.IsActive))
            return;

        notifyIcon.Visible = true;
        notifyIcon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Info);

        hideTimer.Stop();
        hideTimer.Start();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        hideTimer.Stop();
        hideTimer.Tick -= OnHideTimerTick;

        notifyIcon.BalloonTipClicked -= OnNotificationClicked;
        notifyIcon.Dispose();
        applicationIcon.Dispose();
    }

    private static Icon GetApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;

        return executablePath is not null
                   ? Icon.ExtractAssociatedIcon(executablePath) ?? (Icon)SystemIcons.Application.Clone()
                   : (Icon)SystemIcons.Application.Clone();
    }

    private static void OnNotificationClicked(object? sender, EventArgs e)
    {
        var window = Application.Current.MainWindow;

        if (window is null)
            return;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Show();
        window.Activate();
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        hideTimer.Stop();
        notifyIcon.Visible = false;
    }
}
