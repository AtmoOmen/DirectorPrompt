using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed class AgentTaskSettingViewModel : ObservableObject
{
    public AgentTaskConfig Config { get; }

    public AgentTaskType TaskType => Config.TaskType;

    public string TaskTypeDisplay => Loc.Get($"Agent.Task.{Config.TaskType}");

    public ObservableCollection<MCPServerSelectionViewModel> MCPServers { get; } = [];

    public AgentTaskSettingViewModel(AgentTaskConfig config, IEnumerable<MCPServerConfig>? mcpServers = null)
    {
        Config = config;

        if (mcpServers is not null)
            foreach (var server in mcpServers)
                MCPServers.Add(new MCPServerSelectionViewModel(config, server));
    }

    public void AddMCPServer(MCPServerConfig server) =>
        MCPServers.Add(new MCPServerSelectionViewModel(Config, server));

    public void RemoveMCPServer(string serverID)
    {
        Config.MCPServerIDs.RemoveAll(id => id == serverID);

        var item = MCPServers.FirstOrDefault(server => server.Server.ID == serverID);

        if (item is not null)
            MCPServers.Remove(item);
    }

    public string ModelConfigID
    {
        get => Config.ModelConfigID;
        set
        {
            if (Config.ModelConfigID != value)
            {
                Config.ModelConfigID = value;
                OnPropertyChanged();
            }
        }
    }

    public string? PromptID
    {
        get => Config.PromptID;
        set
        {
            if (Config.PromptID != value)
            {
                Config.PromptID = value;
                OnPropertyChanged();
            }
        }
    }

    public string TaskIcon => TaskType switch
    {
        AgentTaskType.Narrator => "Message",
        AgentTaskType.MemoryUpdate => "SaveLocal",
        AgentTaskType.Scene => "Pictures",
        _ => "Bullets"
    };
}
