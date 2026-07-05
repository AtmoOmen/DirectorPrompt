using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DirectorPrompt.Infrastructure.Logging;

public static class LoggingConfiguration
{
    public static Logger CreateLogger(string logDirectory = "logs")
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, logDirectory);

        Directory.CreateDirectory(fullPath);

        return new LoggerConfiguration()
               .MinimumLevel.Information()
               .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
               .MinimumLevel.Override("System", LogEventLevel.Warning)
               .Enrich.FromLogContext()
               .WriteTo.Async
               (sink =>
                    sink.File
                    (
                        Path.Combine(fullPath, "director-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                    )
               )
               .CreateLogger();
    }

    public static IHostBuilder UseDirectorPromptLogging(this IHostBuilder hostBuilder) =>
        hostBuilder.UseSerilog();
}
