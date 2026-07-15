using System.IO;
using System.Windows;
using System.Windows.Threading;
using DirectorPrompt.Agents;
using DirectorPrompt.Agents.Config;
using DirectorPrompt.Agents.Pipeline;
using DirectorPrompt.Agents.Retrieval;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Infrastructure;
using DirectorPrompt.Infrastructure.AI;
using DirectorPrompt.Infrastructure.Extensions;
using DirectorPrompt.Infrastructure.Localization;
using DirectorPrompt.Infrastructure.Logging;
using DirectorPrompt.Infrastructure.Repositories;
using DirectorPrompt.Localization;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;
using DirectorPrompt.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Velopack;
#if RELEASE
using DirectorPrompt.Update;
#endif

namespace DirectorPrompt;

public partial class App
{
    private IHost? host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        VelopackApp.Build().Run();

        base.OnStartup(e);

        Log.Logger = LoggingConfiguration.CreateLogger();

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            UserSettingsExtension.MigrateIfNeeded();

            host = Host.CreateDefaultBuilder()
                       .UseContentRoot(AppContext.BaseDirectory)
                       .UseSerilog()
                       .ConfigureAppConfiguration
                       (config =>
                           {
                               config.SetBasePath(AppContext.BaseDirectory);
                               config.AddJsonFile("appsettings.json",        false, true);
                               config.AddJsonFile(AppPaths.UserSettingsPath, true,  true);
                           }
                       )
                       .ConfigureServices(ConfigureServices)
                       .Build();

            await host.StartAsync();

            var localizationService = host.Services.GetRequiredService<ILocalizationService>();
            Loc.Instance.SetService(localizationService);

            var migrator = host.Services.GetRequiredService<SchemaMigrator>();
            await migrator.MigrateAsync();

#if RELEASE
            var shouldContinue = await CheckForUpdatesAsync();

            if (!shouldContinue)
                return;
#endif

            var mainWindow = host.Services.GetRequiredService<MainWindow>();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "应用启动失败");
            Log.CloseAndFlush();

            MessageBox.Show
            (
                $"启动失败: {ex.Message}\n\n{ex.StackTrace}",
                "DirectorPrompt 启动错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            host.StopAsync().GetAwaiter().GetResult();

            if (host is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            else
                host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
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
                                                     (changelog, version) =>
                                                     {
                                                         updateWindow.Hide();

                                                         var changelogWindow = new ChangelogWindow(changelog, version)
                                                         {
                                                             Owner = updateWindow
                                                         };
                                                         changelogWindow.ShowDialog();

                                                         return Task.CompletedTask;
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

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "未处理的异常");

        MessageBox.Show
        (
            $"发生未处理异常:\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "DirectorPrompt 错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error
        );

        e.Handled = false;
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        var configuration = context.Configuration;

        Directory.CreateDirectory(AppPaths.DataDirectory);

        var connectionString  = $"Data Source={AppPaths.DatabasePath}";
        var connectionFactory = new SqliteConnectionFactory(connectionString);

        services.AddSingleton(connectionFactory);
        services.AddSingleton<SqliteDatabaseScheduler>();
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
        services.AddSingleton<IConditionEngine, ConditionEngine>();
        services.AddSingleton<ICharacterCategoryResolver, CharacterCategoryResolver>();
        services.AddSingleton<ISystemStateTransformer, SystemStateTransformer>();

        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IModelConnectionTester, ModelConnectionTester>();

        var orchestratorConfig = configuration.GetSection("Orchestrator").Get<OrchestratorConfig>() ?? new OrchestratorConfig();
        services.AddSingleton(orchestratorConfig);
        services.AddSingleton<AgentConfigResolver>();

        var userSettings = configuration.Get<UserSettings>() ?? new UserSettings();
        services.AddSingleton(userSettings);

        services.AddSingleton<IEmbeddingServiceFactory, EmbeddingServiceFactory>();
        services.AddSingleton<EmbeddingIndexService>();
        services.AddSingleton<KnowledgeRetrievalService>();
        services.AddSingleton<MemoryRetrievalService>();

        RegisterLocalization(services, configuration);

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
        services.AddSingleton<NotificationService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<ProjectEditViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProjectEditWindow>();
        services.AddTransient<SettingsWindow>();
    }

    private static void RegisterLocalization(IServiceCollection services, IConfiguration configuration)
    {
        var langDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Langs");

        var preferredLanguage = configuration["Localization:Language"] ?? "zh-CN";

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
}
