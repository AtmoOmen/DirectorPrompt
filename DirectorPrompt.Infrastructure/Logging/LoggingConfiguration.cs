using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DirectorPrompt.Infrastructure.Logging;

public static class LoggingConfiguration
{
    public static Logger CreateLogger(string? logDirectory = null)
    {
        var fullPath = logDirectory ?? AppPaths.LogDirectory;

        Directory.CreateDirectory(fullPath);

        var logPath = Path.Combine(fullPath, "directorprompt.log");

#if DEBUG
        const LogEventLevel MINIMUM_LEVEL = LogEventLevel.Debug;
#else
        const LogEventLevel MINIMUM_LEVEL = LogEventLevel.Information;
#endif

        return new LoggerConfiguration()
               .MinimumLevel.Is(MINIMUM_LEVEL)
               .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
               .MinimumLevel.Override("System", LogEventLevel.Warning)
               .Enrich.FromLogContext()
               .WriteTo.Async
               (
                   sink =>
                       sink.File
                       (
                           logPath,
                           rollingInterval: RollingInterval.Day,
                           fileSizeLimitBytes: 5 * 1024 * 1024,
                           rollOnFileSizeLimit: true,
                           retainedFileCountLimit: 3,
                           outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                       ),
                   1024,
                   false
               )
               .CreateLogger();
    }

    public static IHostBuilder UseDirectorPromptLogging(this IHostBuilder hostBuilder) =>
        hostBuilder.UseSerilog();
}
