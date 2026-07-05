using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.ViewModels;

public sealed partial class DirectiveItemViewModel : ObservableObject
{
    [ObservableProperty]
    private DirectiveType type;

    [ObservableProperty]
    private string content = string.Empty;

    [ObservableProperty]
    private int order;

    public string TypeDisplay => Type switch
    {
        DirectiveType.Plot => "剧情",
        DirectiveType.Tone => "基调",
        DirectiveType.TemporaryConstraint => "临时约束",
        DirectiveType.SceneChange => "时间/场景变更",
        _ => Type.ToString()
    };
}

public sealed partial class DirectiveInputViewModel : ObservableObject
{
    [ObservableProperty]
    private DirectiveType selectedType = DirectiveType.Plot;

    [ObservableProperty]
    private string inputContent = string.Empty;

    [ObservableProperty]
    private bool isSending;

    public ObservableCollection<DirectiveItemViewModel> Directives { get; } = [];

    [RelayCommand]
    public void AddDirective()
    {
        if (string.IsNullOrWhiteSpace(InputContent))
        {
            return;
        }

        Directives.Add(new DirectiveItemViewModel
        {
            Type = SelectedType,
            Content = InputContent.Trim(),
            Order = Directives.Count + 1
        });

        InputContent = string.Empty;
    }

    [RelayCommand]
    public void RemoveDirective(DirectiveItemViewModel item)
    {
        Directives.Remove(item);
        ReorderDirectives();
    }

    [RelayCommand]
    public void MoveUp(DirectiveItemViewModel item)
    {
        var index = Directives.IndexOf(item);

        if (index <= 0)
        {
            return;
        }

        Directives.Move(index, index - 1);
        ReorderDirectives();
    }

    [RelayCommand]
    public void MoveDown(DirectiveItemViewModel item)
    {
        var index = Directives.IndexOf(item);

        if (index < 0 || index >= Directives.Count - 1)
        {
            return;
        }

        Directives.Move(index, index + 1);
        ReorderDirectives();
    }

    public void Clear()
    {
        Directives.Clear();
    }

    private void ReorderDirectives()
    {
        for (var i = 0; i < Directives.Count; i++)
        {
            Directives[i].Order = i + 1;
        }
    }
}
