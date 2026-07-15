using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.Domain.Services;

public interface IUserSettingsStore
{
    UserSettings Load();

    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);

    bool MigrateIfNeeded();
}
