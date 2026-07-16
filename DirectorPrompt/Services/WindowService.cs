using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Localization;
using DirectorPrompt.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DirectorPrompt.Services;

public sealed class WindowService
(
    IServiceProvider    serviceProvider,
    UserSettings        userSettings,
    ILanSharingService  lanSharingService
) : IWindowService
{
    public Task<string?> InputAsync(string title, string prompt, string defaultValue) =>
        PromptDialog.InputAsync(App.GetActiveWindow(), title, prompt, defaultValue);

    public async Task<bool> EditProjectAsync(Project project)
    {
        var window = serviceProvider.GetRequiredService<ProjectEditWindow>();
        await window.ViewModel.LoadFromProjectAsync(project);
        var owner = App.GetActiveWindow();

        return owner is not null && await window.ShowDialog<bool>(owner);
    }

    public async Task ShowSettingsAsync()
    {
        var window = serviceProvider.GetRequiredService<SettingsWindow>();
        var owner  = App.GetActiveWindow();
        var saved  = false;

        if (owner is not null)
            saved = await window.ShowDialog<bool>(owner);
        else
            window.Show();

        if (!saved)
            return;

        try
        {
            await lanSharingService.ApplyAsync(userSettings.RemoteControl.IsLanSharingEnabled);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "应用局域网共享设置失败");
            await PromptDialog.ShowErrorAsync
            (
                owner,
                Loc.Get("Settings.Title"),
                Loc.Get("Settings.SaveFailed", ex.Message)
            );
        }
    }
}
