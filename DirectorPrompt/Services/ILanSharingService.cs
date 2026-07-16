using System.ComponentModel;

namespace DirectorPrompt.Services;

public interface ILanSharingService : INotifyPropertyChanged
{
    Uri? Endpoint { get; }

    bool IsActive { get; }

    Task ApplyAsync(bool enabled, CancellationToken cancellationToken = default);
}
