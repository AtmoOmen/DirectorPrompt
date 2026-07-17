using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DirectorPrompt.ViewModels;

public sealed partial class MCPKeyValueViewModel : ObservableObject
{
    private readonly Action<MCPKeyValueViewModel> remove;

    [ObservableProperty]
    public partial string Key { get; set; }

    [ObservableProperty]
    public partial string Value { get; set; }

    public MCPKeyValueViewModel(string key, string value, Action<MCPKeyValueViewModel> remove)
    {
        Key = key;
        Value = value;
        this.remove = remove;
    }

    [RelayCommand]
    private void Remove() =>
        remove(this);
}
