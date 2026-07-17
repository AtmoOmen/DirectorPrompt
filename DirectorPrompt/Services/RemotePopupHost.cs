using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Embedding;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace DirectorPrompt.Services;

public static class RemotePopupHost
{
    private static Canvas? popupLayer;
    private static PopupEntry? currentPopup;

    public static void Attach(Canvas layer)
    {
        CloseCurrentPopup(true);
        popupLayer = layer;
    }

    public static void Detach(Canvas layer)
    {
        if (!ReferenceEquals(popupLayer, layer))
            return;

        CloseCurrentPopup(true);
        popupLayer = null;
    }

    public static bool IsRemote(Control control) =>
        popupLayer is not null && TopLevel.GetTopLevel(control) is EmbeddableControlRoot;

    public static bool Show
    (
        Control owner,
        Control content,
        double width,
        Action<Control> restoreContent
    )
    {
        if (!IsRemote(owner) || popupLayer is null)
            return false;

        CloseCurrentPopup(true);

        if (!double.IsNaN(width) && width > 0)
            content.Width = width;

        var dismissLayer = new Border
        {
            Background = Avalonia.Media.Brushes.Transparent,
            IsHitTestVisible = true
        };
        dismissLayer.PointerPressed += OnDismissLayerPressed;
        popupLayer.Children.Add(dismissLayer);
        popupLayer.Children.Add(content);
        currentPopup = new PopupEntry(owner, content, dismissLayer, restoreContent);
        owner.LayoutUpdated += OnOwnerLayoutUpdated;
        PositionCurrentPopup();
        return true;
    }

    public static Control? Hide(Control owner)
    {
        if (currentPopup is null || !ReferenceEquals(currentPopup.Owner, owner))
            return null;

        var content = currentPopup.Content;
        CloseCurrentPopup(false);
        return content;
    }

    private static void OnOwnerLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is Control owner && currentPopup?.Owner == owner)
            PositionCurrentPopup();
    }

    private static void PositionCurrentPopup()
    {
        if (popupLayer is null || currentPopup is null)
            return;

        var point = currentPopup.Owner.TranslatePoint
        (
            new Point(0, currentPopup.Owner.Bounds.Height),
            popupLayer
        );

        if (point is null)
            return;

        var left = Math.Max(8, point.Value.X);
        var top  = Math.Max(8, point.Value.Y);

        if (popupLayer.Bounds.Width > 0 && currentPopup.Content.Width > popupLayer.Bounds.Width - 16)
            currentPopup.Content.Width = Math.Max(0, popupLayer.Bounds.Width - 16);

        currentPopup.DismissLayer.Width  = Math.Max(0, popupLayer.Bounds.Width);
        currentPopup.DismissLayer.Height = Math.Max(0, popupLayer.Bounds.Height);

        if (left + currentPopup.Content.Width > popupLayer.Bounds.Width - 8)
            left = Math.Max(8, popupLayer.Bounds.Width - currentPopup.Content.Width - 8);

        Canvas.SetLeft(currentPopup.Content, left);
        Canvas.SetTop(currentPopup.Content, top);
        currentPopup.Content.SetValue(Visual.ZIndexProperty, 2000);
        currentPopup.DismissLayer.SetValue(Visual.ZIndexProperty, 1999);
    }

    private static void OnDismissLayerPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseCurrentPopup(true);
        e.Handled = true;
    }

    private static void CloseCurrentPopup(bool restoreContent)
    {
        if (currentPopup is null)
            return;

        currentPopup.Owner.LayoutUpdated -= OnOwnerLayoutUpdated;
        currentPopup.DismissLayer.PointerPressed -= OnDismissLayerPressed;
        popupLayer?.Children.Remove(currentPopup.Content);
        popupLayer?.Children.Remove(currentPopup.DismissLayer);

        if (restoreContent)
            currentPopup.RestoreContent(currentPopup.Content);

        currentPopup = null;
    }

    private sealed record PopupEntry
    (
        Control Owner,
        Control Content,
        Border DismissLayer,
        Action<Control> RestoreContent
    );
}
