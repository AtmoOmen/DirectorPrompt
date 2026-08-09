using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Embedding;
using Avalonia.Input;
using Avalonia.Media;

namespace DirectorPrompt.Services;

public static class RemotePopupHost
{
    private static Canvas?     popupLayer;
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
        Control         owner,
        Control         content,
        double          width,
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
            Background       = Brushes.Transparent,
            IsHitTestVisible = true
        };
        dismissLayer.PointerPressed += OnDismissLayerPressed;
        popupLayer.Children.Add(dismissLayer);
        popupLayer.Children.Add(content);
        currentPopup        =  new PopupEntry(owner, content, dismissLayer, restoreContent);
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

        var layerWidth  = popupLayer.Bounds.Width;
        var layerHeight = popupLayer.Bounds.Height;

        if (layerWidth <= 0 || layerHeight <= 0)
            return;

        var ownerTopLeft = currentPopup.Owner.TranslatePoint(default, popupLayer);

        if (ownerTopLeft is null)
            return;

        var availableSize = new Size(Math.Max(1, layerWidth - 16), Math.Max(1, layerHeight - 16));

        if (currentPopup.Content.Width > availableSize.Width)
            currentPopup.Content.Width = availableSize.Width;

        currentPopup.Content.Measure(availableSize);

        var popupWidth  = Math.Min(currentPopup.Content.DesiredSize.Width,  availableSize.Width);
        var popupHeight = Math.Min(currentPopup.Content.DesiredSize.Height, availableSize.Height);
        var ownerTop    = ownerTopLeft.Value.Y;
        var ownerBottom = ownerTop + currentPopup.Owner.Bounds.Height;
        var spaceAbove  = ownerTop - 8;
        var spaceBelow  = layerHeight - ownerBottom - 8;
        var left        = Math.Max(8, ownerTopLeft.Value.X);
        var top         = popupHeight > spaceBelow && spaceAbove > spaceBelow ?
                              ownerTop - popupHeight :
                              ownerBottom;

        currentPopup.DismissLayer.Width  = layerWidth;
        currentPopup.DismissLayer.Height = layerHeight;

        if (left + popupWidth > layerWidth - 8)
            left = Math.Max(8, layerWidth - popupWidth - 8);

        top = Math.Clamp(top, 8, Math.Max(8, layerHeight - popupHeight - 8));

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

        currentPopup.Owner.LayoutUpdated         -= OnOwnerLayoutUpdated;
        currentPopup.DismissLayer.PointerPressed -= OnDismissLayerPressed;
        popupLayer?.Children.Remove(currentPopup.Content);
        popupLayer?.Children.Remove(currentPopup.DismissLayer);

        if (restoreContent)
            currentPopup.RestoreContent(currentPopup.Content);

        currentPopup = null;
    }

    private sealed record PopupEntry
    (
        Control         Owner,
        Control         Content,
        Border          DismissLayer,
        Action<Control> RestoreContent
    );
}
