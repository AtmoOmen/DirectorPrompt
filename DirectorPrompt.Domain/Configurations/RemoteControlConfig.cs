namespace DirectorPrompt.Domain.Configurations;

public sealed class RemoteControlConfig
{
    public bool IsLanSharingEnabled { get; set; }

    public int Port { get; set; } = 32145;
}
