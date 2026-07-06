using System.Text.Json;
using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.Infrastructure.Extensions;

public static class UserSettingsExtension
{
    extension(UserSettings settings)
    {
        public async Task SaveAsync()
        {
            var json = JsonSerializer.Serialize(settings, UserSettings.JSONOptions);

            Directory.CreateDirectory(AppPaths.DataDirectory);
            await File.WriteAllTextAsync(AppPaths.UserSettingsPath, json);
        }
    }
}
