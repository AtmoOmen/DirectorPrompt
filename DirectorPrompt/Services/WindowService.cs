using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Localization;
using DirectorPrompt.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DirectorPrompt.Services;

public sealed class WindowService
(
    IServiceProvider              serviceProvider,
    UserSettings                  userSettings,
    ILanSharingService            lanSharingService,
    RemoteInteractionRouter       remoteInteractionRouter,
    IProjectEditWindowCoordinator projectEditWindowCoordinator
) : IWindowService
{
    public Task<string?> InputAsync(string title, string prompt, string defaultValue)
    {
        var remoteWindowService = remoteInteractionRouter.Consume();

        Log.Debug
        (
            "显示输入对话框: 来源={Source}, 默认值长度={DefaultValueLength}",
            remoteWindowService is null ? "本地" : "远程",
            defaultValue.Length
        );

        return remoteWindowService is not null ?
                   remoteWindowService.InputAsync(title, prompt, defaultValue) :
                   PromptDialog.InputAsync(App.GetActiveWindow(), title, prompt, defaultValue);
    }

    public async Task<bool> EditProjectAsync(Project project)
    {
        var remoteWindowService = remoteInteractionRouter.Consume();

        if (remoteWindowService is not null)
        {
            Log.Information("通过远程交互打开项目编辑窗口: 项目={ProjectID}", project.ID);
            return await remoteWindowService.EditProjectAsync(project);
        }

        Log.Information("打开本地项目编辑窗口: 项目={ProjectID}", project.ID);

        var window = serviceProvider.GetRequiredService<ProjectEditWindow>();
        await window.ViewModel.LoadFromProjectAsync(project);
        var owner = App.GetActiveWindow();

        projectEditWindowCoordinator.Register(project.ID, window.CloseWithoutSaving);

        try
        {
            var saved = owner is not null && await window.ShowDialog<bool>(owner);
            Log.Information("本地项目编辑窗口已关闭: 项目={ProjectID}, 已保存={Saved}", project.ID, saved);
            return saved;
        }
        finally
        {
            projectEditWindowCoordinator.Unregister(project.ID, window.CloseWithoutSaving);
        }
    }

    public async Task ShowSettingsAsync()
    {
        var remoteWindowService = remoteInteractionRouter.Consume();

        if (remoteWindowService is not null)
        {
            Log.Information("通过远程交互打开设置窗口");
            await remoteWindowService.ShowSettingsAsync();
            return;
        }

        Log.Information("打开本地设置窗口");

        var window = serviceProvider.GetRequiredService<SettingsWindow>();
        var owner  = App.GetActiveWindow();
        var saved  = false;

        if (owner is not null)
            saved = await window.ShowDialog<bool>(owner);
        else
            window.Show();

        if (!saved)
        {
            Log.Information("本地设置窗口未保存即关闭");
            return;
        }

        try
        {
            await lanSharingService.ApplyAsync(userSettings.RemoteControl.IsLanSharingEnabled);
            Log.Information("本地设置已应用局域网共享: 已启用={Enabled}", userSettings.RemoteControl.IsLanSharingEnabled);
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
