using System.Windows;
using System.Windows.Media;
using DirectorPrompt.Localization;
using Wpf.Ui.Controls;

namespace DirectorPrompt.Views;

public partial class CorrectionCompareWindow : FluentWindow
{
    public CorrectionCompareWindow() =>
        InitializeComponent();

    public static bool Show
    (
        Window owner,
        string original,
        string corrected,
        string leftHeader,
        string rightHeader
    )
    {
        var window = new CorrectionCompareWindow();

        window.DialogTitleBar.Title = Loc.Get("Dialog.CorrectResultTitle");
        window.RejectButton.Content = Loc.Get("Common.Reject");
        window.AcceptButton.Content = Loc.Get("Common.Accept");
        window.Owner                = owner;

        window.DiffView.OldTextHeader = leftHeader;
        window.DiffView.NewTextHeader = rightHeader;
        window.DiffView.OldText       = original;
        window.DiffView.NewText       = corrected;
        window.DiffView.FontFamily    = new FontFamily("Microsoft YaHei UI, Consolas, Courier New");

        window.DiffView.InsertedBackground = new SolidColorBrush(Color.FromArgb(60, 46,  160, 67));
        window.DiffView.DeletedBackground  = new SolidColorBrush(Color.FromArgb(60, 248, 81,  73));

        window.ShowDialog();

        return window.DialogResult == true;
    }

    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnRejectClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
