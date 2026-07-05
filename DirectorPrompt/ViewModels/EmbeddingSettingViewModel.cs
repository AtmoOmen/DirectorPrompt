using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class EmbeddingSettingViewModel : ObservableObject
{
    [ObservableProperty]
    private string provider = "openai";

    [ObservableProperty]
    private string endpoint = string.Empty;

    private string apiKey = string.Empty;

    public string APIKey
    {
        get => apiKey;
        set => SetProperty(ref apiKey, value);
    }

    [ObservableProperty]
    private string modelName = "text-embedding-3-small";

    [ObservableProperty]
    private bool isTestingConnection;

    [ObservableProperty]
    private string connectionMessage = string.Empty;

    [ObservableProperty]
    private bool? connectionSuccess;
}
