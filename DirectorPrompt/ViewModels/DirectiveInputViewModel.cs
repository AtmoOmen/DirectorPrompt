using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.ViewModels;

public sealed partial class DirectiveItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial DirectiveType Type { get; set; }

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int Order { get; set; }

    [ObservableProperty]
    public partial int? TTL { get; set; }

    public string TypeDisplay => Type switch
    {
        DirectiveType.Plot                => "剧情",
        DirectiveType.Tone                => "基调",
        DirectiveType.TemporaryConstraint => "临时约束",
        DirectiveType.SceneChange         => "时间/场景变更",
        _                                 => Type.ToString()
    };

    public bool HasTTL => Type is DirectiveType.Tone or DirectiveType.TemporaryConstraint;
}

public sealed partial class DirectiveInputViewModel : ObservableObject
{
    [ObservableProperty]
    public partial DirectiveType SelectedType { get; set; } = DirectiveType.Plot;

    [ObservableProperty]
    public partial string InputContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int? InputTTL { get; set; }

    [ObservableProperty]
    public partial bool IsSending { get; set; }

    public ObservableCollection<DirectiveItemViewModel> Directives { get; } = [];

    public bool InputHasTTL =>
        SelectedType is DirectiveType.Tone or DirectiveType.TemporaryConstraint;

    partial void OnSelectedTypeChanged(DirectiveType value)
    {
        OnPropertyChanged(nameof(InputHasTTL));

        if (!InputHasTTL)
            InputTTL = null;
        else if (InputTTL is null)
            InputTTL = 5;
    }

    [RelayCommand]
    public void AddDirective()
    {
        if (string.IsNullOrWhiteSpace(InputContent))
            return;

        Directives.Add
        (
            new DirectiveItemViewModel
            {
                Type    = SelectedType,
                Content = InputContent.Trim(),
                Order   = Directives.Count + 1,
                TTL = InputHasTTL ?
                          InputTTL :
                          null
            }
        );

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
            return;

        Directives.Move(index, index - 1);
        ReorderDirectives();
    }

    [RelayCommand]
    public void MoveDown(DirectiveItemViewModel item)
    {
        var index = Directives.IndexOf(item);

        if (index < 0 || index >= Directives.Count - 1)
            return;

        Directives.Move(index, index + 1);
        ReorderDirectives();
    }

    public void Clear() =>
        Directives.Clear();

    private void ReorderDirectives()
    {
        for (var i = 0; i < Directives.Count; i++)
            Directives[i].Order = i + 1;
    }
}
