using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.ViewModels;

public sealed partial class MCPServerSelectionViewModel : ObservableObject
{
    private readonly AgentTaskConfig config;

    public MCPServerConfig Server { get; }

    public string DisplayName => Server.DisplayName;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public MCPServerSelectionViewModel(AgentTaskConfig config, MCPServerConfig server)
    {
        this.config = config;
        Server = server;
        IsSelected = config.MCPServerIDs.Contains(server.ID);
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            if (!config.MCPServerIDs.Contains(Server.ID))
                config.MCPServerIDs.Add(Server.ID);
        }
        else
            config.MCPServerIDs.RemoveAll(id => id == Server.ID);
    }
}
