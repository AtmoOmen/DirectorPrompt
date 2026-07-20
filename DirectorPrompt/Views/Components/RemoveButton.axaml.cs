using Avalonia.Controls;

namespace DirectorPrompt.Views.Components;

public partial class RemoveButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public RemoveButton() =>
        InitializeComponent();
}
