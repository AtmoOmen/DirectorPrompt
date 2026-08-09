using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed partial class DirectiveItemViewModel : ObservableObject
{
    private int finiteTTL = 5;

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
        DirectiveType.Plot        => Loc.Get("Directive.Type.Plot"),
        DirectiveType.Constraint  => Loc.Get("Directive.Type.Constraint"),
        DirectiveType.SceneChange => Loc.Get("Directive.Type.SceneChange"),
        _                         => Type.ToString()
    };

    public bool HasTTL => Type == DirectiveType.Constraint;

    public bool IsPermanent
    {
        get => TTL is null;
        set
        {
            if (value == IsPermanent)
                return;

            TTL = value ? null : finiteTTL;
        }
    }

    partial void OnTTLChanged(int? value)
    {
        if (value is > 0)
            finiteTTL = value.Value;

        OnPropertyChanged(nameof(IsPermanent));
    }
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
    public partial bool InputIsPermanent { get; set; }

    [ObservableProperty]
    public partial bool IsSending { get; set; }

    public ObservableCollection<DirectiveItemViewModel> Directives { get; } = [];

    public bool InputHasTTL => SelectedType == DirectiveType.Constraint;

    partial void OnSelectedTypeChanged(DirectiveType value)
    {
        OnPropertyChanged(nameof(InputHasTTL));

        if (InputHasTTL && InputTTL is null)
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
                TTL = InputHasTTL && !InputIsPermanent ?
                          InputTTL ?? 5 :
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
