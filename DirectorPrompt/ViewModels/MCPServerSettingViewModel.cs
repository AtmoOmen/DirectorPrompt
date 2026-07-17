using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Domain.Configurations;

namespace DirectorPrompt.ViewModels;

public sealed partial class MCPServerSettingViewModel : ObservableObject
{
    public MCPServerConfig Config { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStdio))]
    [NotifyPropertyChangedFor(nameof(IsStreamableHTTP))]
    public partial MCPTransportType Transport { get; set; }

    [ObservableProperty]
    public partial string Command { get; set; }

    [ObservableProperty]
    public partial string ArgumentsText { get; set; }

    [ObservableProperty]
    public partial string WorkingDirectory { get; set; }

    [ObservableProperty]
    public partial string Endpoint { get; set; }

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    [ObservableProperty]
    public partial string InspectionMessage { get; set; } = string.Empty;

    public bool IsStdio => Transport == MCPTransportType.Stdio;

    public bool IsStreamableHTTP => Transport == MCPTransportType.StreamableHttp;

    public ObservableCollection<MCPKeyValueViewModel> EnvironmentVariables { get; } = [];

    public ObservableCollection<MCPKeyValueViewModel> Headers { get; } = [];

    public MCPServerSettingViewModel(MCPServerConfig config)
    {
        Config = config;
        DisplayName = config.DisplayName;
        Enabled = config.Enabled;
        Transport = config.Transport;
        Command = config.Command;
        ArgumentsText = string.Join(Environment.NewLine, config.Arguments);
        WorkingDirectory = config.WorkingDirectory;
        Endpoint = config.Endpoint;

        foreach (var pair in config.Environment)
            EnvironmentVariables.Add(new MCPKeyValueViewModel(pair.Key, pair.Value, item => EnvironmentVariables.Remove(item)));

        foreach (var pair in config.Headers)
            Headers.Add(new MCPKeyValueViewModel(pair.Key, pair.Value, item => Headers.Remove(item)));
    }

    public void Apply()
    {
        Config.DisplayName = DisplayName.Trim();
        Config.Enabled = Enabled;
        Config.Transport = Transport;
        Config.Command = Command.Trim();
        Config.Arguments = ArgumentsText.Split
        (
            ["\r\n", "\n"],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        ).ToList();
        Config.WorkingDirectory = WorkingDirectory.Trim();
        Config.Endpoint = Endpoint.Trim();
        Config.Environment = ToDictionary(EnvironmentVariables);
        Config.Headers = ToDictionary(Headers);
    }

    [RelayCommand]
    private void AddEnvironment() =>
        EnvironmentVariables.Add(new MCPKeyValueViewModel(string.Empty, string.Empty, item => EnvironmentVariables.Remove(item)));

    [RelayCommand]
    private void AddHeader() =>
        Headers.Add(new MCPKeyValueViewModel(string.Empty, string.Empty, item => Headers.Remove(item)));

    private static Dictionary<string, string> ToDictionary(IEnumerable<MCPKeyValueViewModel> values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in values)
        {
            if (!string.IsNullOrWhiteSpace(item.Key))
                result[item.Key.Trim()] = item.Value;
        }

        return result;
    }
}
