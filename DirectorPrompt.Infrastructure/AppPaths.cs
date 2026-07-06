namespace DirectorPrompt.Infrastructure;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine
    (
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DirectorPrompt"
    );

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    public static string UserSettingsPath { get; } = Path.Combine(DataDirectory, "usersettings.json");

    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "director.db");
}
