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

        var logPath    = Path.Combine(fullPath, "directorprompt.log");
        var oldLogPath = Path.Combine(fullPath, "directorprompt.old.log");

        if (File.Exists(logPath))
            File.Move(logPath, oldLogPath, true);

        return new LoggerConfiguration()
               .MinimumLevel.Is(LogEventLevel.Debug)
               .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
               .MinimumLevel.Override("System", LogEventLevel.Warning)
               .Enrich.FromLogContext()
               .WriteTo.Async
               (
                   sink =>
                       sink.File
                       (
                           logPath,
                           outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                       ),
                   1024
               )
               .CreateLogger();
    }

    public static IHostBuilder UseDirectorPromptLogging(this IHostBuilder hostBuilder) =>
        hostBuilder.UseSerilog();
}
