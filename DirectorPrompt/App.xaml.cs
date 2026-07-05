using System.IO;
using System.Windows;
using System.Windows.Threading;
using DirectorPrompt.Infrastructure;
using DirectorPrompt.Infrastructure.AI;
using DirectorPrompt.Infrastructure.Logging;
using DirectorPrompt.Infrastructure.Repositories;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.ViewModels;
using DirectorPrompt.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace DirectorPrompt;

public partial class App : Application
{
    private IHost? host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = LoggingConfiguration.CreateLogger();

        try
        {
            host = Host.CreateDefaultBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .UseSerilog()
                .ConfigureAppConfiguration(config =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices(ConfigureServices)
                .Build();

            host.StartAsync().GetAwaiter().GetResult();

            var migrator = host.Services.GetRequiredService<SchemaMigrator>();
            migrator.MigrateAsync().GetAwaiter().GetResult();

            var mainWindow = host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "应用启动失败");
            Log.CloseAndFlush();

            MessageBox.Show(
                $"启动失败: {ex.Message}\n\n{ex.StackTrace}",
                "DirectorPrompt 启动错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            host.StopAsync().GetAwaiter().GetResult();
            host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "未处理的异常");

        MessageBox.Show(
            $"发生未处理异常:\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "DirectorPrompt 错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = false;
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        var configuration = context.Configuration;

        var dbPath = configuration["Database:Path"] ?? "director.db";

        var fullDBPath = Path.IsPathRooted(dbPath)
            ? dbPath
            : Path.Combine(AppContext.BaseDirectory, dbPath);

        var dbDirectory = Path.GetDirectoryName(fullDBPath);

        if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        var connectionString = $"Data Source={fullDBPath}";
        var connectionFactory = new SqliteConnectionFactory(connectionString);

        services.AddSingleton(connectionFactory);
        services.AddSingleton<SchemaMigrator>();

        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<ISceneRepository, SceneRepository>();
        services.AddSingleton<IStateRepository, StateRepository>();
        services.AddSingleton<IKnowledgeRepository, KnowledgeRepository>();
        services.AddSingleton<IMemoryRepository, MemoryRepository>();
        services.AddSingleton<ICharacterRepository, CharacterRepository>();
        services.AddSingleton<IEventRepository, EventRepository>();
        services.AddSingleton<IStateSnapshotRepository, StateSnapshotRepository>();
        services.AddSingleton<IDirectiveRepository, DirectiveRepository>();

        services.AddSingleton<ITimelineCalculator, TimelineCalculator>();
        services.AddSingleton<IConditionEngine, ConditionEngine>();

        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IModelConnectionTester, ModelConnectionTester>();

        var orchestratorConfig = configuration.GetSection("Orchestrator").Get<OrchestratorConfig>() ?? new OrchestratorConfig();
        services.AddSingleton(orchestratorConfig);

        var embeddingSection = configuration.GetSection("Embedding");
        var embeddingProvider = embeddingSection["Provider"] ?? "openai";
        var embeddingEndpoint = embeddingSection["Endpoint"] ?? string.Empty;
        var embeddingAPIKey = embeddingSection["ApiKey"];
        var embeddingModel = embeddingSection["ModelName"] ?? "text-embedding-3-small";

        services.AddSingleton<IEmbeddingService>(_ => new EmbeddingService(
            embeddingProvider, embeddingEndpoint, embeddingAPIKey, embeddingModel));

        services.AddSingleton<Orchestrator>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<ProjectEditViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProjectEditWindow>();
        services.AddTransient<SettingsWindow>();
    }
}
