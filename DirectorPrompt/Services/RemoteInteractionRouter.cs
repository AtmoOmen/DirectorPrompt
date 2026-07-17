namespace DirectorPrompt.Services;

public sealed class RemoteInteractionRouter
{
    private IWindowService? remoteWindowService;
    private long            activeInteractionID;

    public void Attach(IWindowService windowService)
    {
        remoteWindowService = windowService;
    }

    public void Detach(IWindowService? windowService)
    {
        if (!ReferenceEquals(remoteWindowService, windowService))
            return;

        remoteWindowService  = null;
        activeInteractionID = 0;
    }

    public long Activate()
    {
        activeInteractionID++;
        return activeInteractionID;
    }

    public void Deactivate(long interactionID)
    {
        if (activeInteractionID == interactionID)
            activeInteractionID = 0;
    }

    public IWindowService? Consume()
    {
        if (activeInteractionID == 0)
            return null;

        activeInteractionID = 0;
        return remoteWindowService;
    }
}
