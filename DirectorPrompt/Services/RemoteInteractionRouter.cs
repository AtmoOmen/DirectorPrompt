using Serilog;

namespace DirectorPrompt.Services;

public sealed class RemoteInteractionRouter
{
    private IWindowService? remoteWindowService;
    private long            activeInteractionID;

    public void Attach(IWindowService windowService)
    {
        remoteWindowService = windowService;
        Log.Information("远程交互路由已连接: 服务={WindowServiceType}", windowService.GetType().Name);
    }

    public void Detach(IWindowService? windowService)
    {
        if (!ReferenceEquals(remoteWindowService, windowService))
            return;

        remoteWindowService = null;
        activeInteractionID = 0;
        Log.Information("远程交互路由已断开");
    }

    public long Activate()
    {
        activeInteractionID++;
        Log.Debug("远程交互已激活: 交互={InteractionID}", activeInteractionID);
        return activeInteractionID;
    }

    public void Deactivate(long interactionID)
    {
        if (activeInteractionID == interactionID)
        {
            activeInteractionID = 0;
            Log.Debug("远程交互已失效: 交互={InteractionID}", interactionID);
        }
    }

    public IWindowService? Consume()
    {
        if (activeInteractionID == 0)
            return null;

        activeInteractionID = 0;
        Log.Debug("远程交互已消费: 服务={WindowServiceType}", remoteWindowService?.GetType().Name);
        return remoteWindowService;
    }
}
