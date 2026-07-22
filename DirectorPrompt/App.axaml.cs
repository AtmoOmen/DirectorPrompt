using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DirectorPrompt.Agents;
using DirectorPrompt.Agents.Config;
using DirectorPrompt.Agents.MCP;
using DirectorPrompt.Agents.Pipeline;
using DirectorPrompt.Agents.Retrieval;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Infrastructure;
using DirectorPrompt.Infrastructure.AI;
using DirectorPrompt.Infrastructure.Localization;
using DirectorPrompt.Infrastructure.Logging;
using DirectorPrompt.Infrastructure.Repositories;
using DirectorPrompt.Infrastructure.Services;
using DirectorPrompt.Localization;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;
using DirectorPrompt.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
#if RELEASE
using DirectorPrompt.Update;
#endif

namespace DirectorPrompt;

public class App : Application
{
    private IHost? host;

    public override void Initialize() =>
        AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        Log.Logger                             =  LoggingConfiguration.CreateLogger();
        desktop.ShutdownMode                   =  ShutdownMode.OnExplicitShutdown;
        desktop.Exit                           += OnExit;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException     += OnUnobservedTaskException;

        Log.Information
        (
            "应用启动: 版本={Version}, 进程={ProcessID}, 基目录={BaseDirectory}",
            typeof(App).Assembly.GetName().Version?.ToString() ?? "未知",
            Environment.ProcessId,
            AppContext.BaseDirectory
        );

        try
        {
            var settingsStore = new UserSettingsStore();
            var settingsMigrated = settingsStore.MigrateIfNeeded();

            Log.Information("用户设置已准备: 已迁移={SettingsMigrated}", settingsMigrated);

            Log.Information("开始构建应用宿主");
            host = Host.CreateDefaultBuilder()
                       .UseContentRoot(AppContext.BaseDirectory)
                       .UseSerilog()
                       .ConfigureAppConfiguration
                       (config =>
                           {
                               config.SetBasePath(AppContext.BaseDirectory);
                               config.AddJsonFile("appsettings.json", false, true);
                           }
                       )
                       .ConfigureServices((_, services) => ConfigureServices(services, settingsStore))
                       .Build();

            var migrator = host.Services.GetRequiredService<SchemaMigrator>();
            await migrator.MigrateAsync();

            await host.StartAsync();
            Log.Information("应用宿主已启动");

            var localizationService = host.Services.GetRequiredService<ILocalizationService>();
            Loc.Instance.SetService(localizationService);
            Log.Information("本地化服务已就绪: 当前语言={Language}", localizationService.CurrentLanguage);

#if RELEASE
            var shouldContinue = await CheckForUpdatesAsync();

            if (!shouldContinue)
            {
                desktop.Shutdown();
                return;
            }
#endif

            var mainWindow = host.Services.GetRequiredService<MainWindow>();
            desktop.MainWindow   = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            mainWindow.Show();
            Log.Information("主窗口已显示");

            var lanSharingService = host.Services.GetRequiredService<ILanSharingService>();

            try
            {
                var isLanSharingEnabled = host.Services.GetRequiredService<UserSettings>().RemoteControl.IsLanSharingEnabled;
                await lanSharingService.ApplyAsync(isLanSharingEnabled);
                Log.Information
                (
                    "局域网共享配置已应用: 已启用={Enabled}, 端点={Endpoint}",
                    isLanSharingEnabled,
                    lanSharingService.Endpoint
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "启动局域网共享失败");
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "应用启动失败");
            await PromptDialog.ShowErrorAsync
            (
                null,
                "DirectorPrompt 启动错误",
                $"启动失败: {ex.Message}\n\n{ex.StackTrace}"
            );
            desktop.Shutdown(1);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Log.Information("应用退出开始: 退出代码={ExitCode}", e.ApplicationExitCode);

        if (host is not null)
        {
            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                _ = host.StopAsync(shutdownTimeout.Token);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "应用宿主停止时发生错误");
            }

            try
            {
                if (host is IAsyncDisposable asyncDisposable)
                {
                    _ = asyncDisposable.DisposeAsync()
                                       .AsTask();
                }
                else
                    host.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "应用宿主释放时发生错误");
            }
        }

        Log.Information("应用退出日志已刷新");
        Log.CloseAndFlush();
    }

#if RELEASE
    private static async Task<bool> CheckForUpdatesAsync()
    {
        var updateWindow = new UpdateWindow();
        updateWindow.Show();

        try
        {
            var orchestrator = new UpdateOrchestrator();

            var (shouldContinue, errorMessage) = await UpdateOrchestrator.RunAsync
                                                 (
                                                     updateWindow.UpdateStatus,
                                                     updateWindow.UpdateProgress,
                                                     async (changelog, version) =>
                                                     {
                                                         var changelogWindow = new ChangelogWindow(changelog, version);
                                                         await changelogWindow.ShowDialog(updateWindow);
                                                     }
                                                 );

            if (errorMessage is not null)
            {
                updateWindow.ShowError(errorMessage);
                await updateWindow.WaitForCloseAsync();
            }

            return shouldContinue;
        }
        finally
        {
            updateWindow.Close();
        }
    }
#endif

    private async void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "未处理的异常");
        await PromptDialog.ShowErrorAsync
        (
            GetActiveWindow(),
            "DirectorPrompt 错误",
            $"发生未处理异常:\n{e.Exception.Message}\n\n{e.Exception.StackTrace}"
        );
        e.Handled = false;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            Log.Fatal(exception, "应用域未处理异常: 即将终止={IsTerminating}", e.IsTerminating);
        else
            Log.Fatal("应用域发生非异常对象的未处理错误: 即将终止={IsTerminating}", e.IsTerminating);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        Log.Error(e.Exception, "未观察到的后台任务异常");

    internal static Window? GetActiveWindow()
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow;
    }

    private static void ConfigureServices(IServiceCollection services, UserSettingsStore settingsStore)
    {
        SQLiteTypeHandlers.Register();
        Directory.CreateDirectory(AppPaths.DataDirectory);

        Log.Information
        (
            "配置应用服务: 数据目录={DataDirectory}, 数据库路径={DatabasePath}",
            AppPaths.DataDirectory,
            AppPaths.DatabasePath
        );

        var connectionString  = $"Data Source={AppPaths.DatabasePath}";
        var connectionFactory = new SQLiteConnectionFactory(connectionString);

        services.AddSingleton(connectionFactory);
        services.AddSingleton<SQLiteDatabaseScheduler>();
        services.AddSingleton<SchemaMigrator>();
        services.AddSingleton<VectorTableManager>();

        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<ISceneRepository, SceneRepository>();
        services.AddSingleton<IStateRepository, StateRepository>();
        services.AddSingleton<IKnowledgeRepository, KnowledgeRepository>();
        services.AddSingleton<IMemoryRepository, MemoryRepository>();
        services.AddSingleton<ICharacterRepository, CharacterRepository>();
        services.AddSingleton<IEventRepository, EventRepository>();
        services.AddSingleton<IDirectiveRepository, DirectiveRepository>();
        services.AddSingleton<IRoundChangeRepository, RoundChangeRepository>();
        services.AddSingleton<IRoundReadSnapshotRepository, RoundReadSnapshotRepository>();
        services.AddSingleton<SidebarSnapshotRepository>();

        services.AddSingleton<IProjectPortService, ProjectPortService>();
        services.AddSingleton<ITimelineCalculator, TimelineCalculator>();
        services.AddSingleton<IExpressionEngine, ExpressionEngine>();
        services.AddSingleton<ICharacterCategoryResolver, CharacterCategoryResolver>();
        services.AddSingleton<ISystemStateTransformer, SystemStateTransformer>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IModelConnectionTester, ModelConnectionTester>();

        var userSettings = settingsStore.Load();
        services.AddSingleton(userSettings);
        services.AddSingleton(userSettings.Orchestrator);
        services.AddSingleton<IUserSettingsStore>(settingsStore);
        services.AddSingleton<AgentConfigResolver>();
        services.AddSingleton<IExternalMCPToolRegistry, ExternalMCPToolRegistry>();
        services.AddSingleton<IAgentToolResolver, AgentToolResolver>();
        services.AddSingleton<IEmbeddingServiceFactory, EmbeddingServiceFactory>();
        services.AddSingleton<EmbeddingIndexService>();
        services.AddSingleton<KnowledgeRetrievalService>();
        services.AddSingleton<MemoryRetrievalService>();

        RegisterLocalization(services, userSettings);

        services.AddSingleton<SceneTools>();
        services.AddSingleton<KnowledgeTools>();
        services.AddSingleton<StateTools>();
        services.AddSingleton<MemoryTools>();
        services.AddSingleton<CharacterTools>();
        services.AddSingleton<DirectiveProcessingStage>();
        services.AddSingleton<PhaseEvaluator>();
        services.AddSingleton<RetrievalStage>();
        services.AddSingleton<GenerationStage>();
        services.AddSingleton<PostProcessingStage>();
        services.AddSingleton<SceneSummaryStage>();
        services.AddSingleton<HistoryBuilder>();
        services.AddSingleton<Orchestrator>();
        services.AddSingleton<DialogHistoryService>();
        services.AddSingleton<SidebarQueryService>();
        RegisterNotificationService(services);
        services.AddSingleton<RemoteInteractionRouter>();
        services.AddSingleton<IProjectContentService, ProjectContentService>();
        services.AddSingleton<IProjectEditWindowCoordinator, ProjectEditWindowCoordinator>();
        services.AddSingleton<MCPProjectTools>();
        services.AddSingleton<DirectorPromptMCPHostedService>();
        services.AddSingleton<IDirectorPromptMCPStatus>
        (serviceProvider => serviceProvider.GetRequiredService<DirectorPromptMCPHostedService>()
        );
        services.AddHostedService
        (serviceProvider => serviceProvider.GetRequiredService<DirectorPromptMCPHostedService>()
        );
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<ILanSharingService, LanSharingService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<ProjectEditViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProjectEditWindow>();
        services.AddTransient<SettingsWindow>();
    }

    private static void RegisterLocalization(IServiceCollection services, UserSettings userSettings)
    {
        var langDirectory     = Path.Combine(AppContext.BaseDirectory, "Assets", "Langs");
        var preferredLanguage = userSettings.Localization.Language;

        if (string.IsNullOrEmpty(preferredLanguage))
            preferredLanguage = "zh-CN";

        Log.Information("注册本地化服务: 首选语言={PreferredLanguage}, 语言目录={LanguageDirectory}", preferredLanguage, langDirectory);

        var options = new LocalizationOptions
        {
            DefaultLanguage  = "zh-CN",
            FileNameResolver = static language => $"{language}.json",
            Source           = new FileLocalizationSource(langDirectory),
            Parser           = new JSONDictionaryLocalizationParser(),
            FallbackResolver = static language => language switch
            {
                "en" => ["zh-CN"],
                _    => []
            },
            EnableHotReload = true,
            ReloadDebounce  = TimeSpan.FromSeconds(3),
            LoggerTag       = nameof(LocalizationService)
        };

        services.AddSingleton<ILocalizationService>
            (_ => new LocalizationService(options, preferredLanguage, preferredLanguage));
    }

    private static void RegisterNotificationService(IServiceCollection services)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<INotificationService, WindowsNotificationService>();
            return;
        }
#else
        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<LinuxNotificationService>();
            services.AddSingleton<INotificationService>
            (serviceProvider => serviceProvider.GetRequiredService<LinuxNotificationService>());
            services.AddHostedService
            (serviceProvider => serviceProvider.GetRequiredService<LinuxNotificationService>());
            return;
        }
#endif

        services.AddSingleton<INotificationService, UnsupportedNotificationService>();
    }
}
