namespace DirectorPrompt.Services;

public interface IProjectEditWindowCoordinator
{
    void Register(long projectID, Action closeWithoutSaving);

    void Unregister(long projectID, Action closeWithoutSaving);

    Task CloseForExternalChangeAsync(long projectID);
}
