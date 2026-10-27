using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VanBaz;

public static class MenuPlacement
{
    public static readonly DependencyProperty AnchoredProperty = DependencyProperty.RegisterAttached("Anchored", typeof(bool), typeof(MenuPlacement), new PropertyMetadata(false, Changed));
    public static void SetAnchored(DependencyObject target, bool value) => target.SetValue(AnchoredProperty, value);
    public static bool GetAnchored(DependencyObject target) => (bool)target.GetValue(AnchoredProperty);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is MenuItem item && e.NewValue is true) item.Loaded += (_, _) =>
        {
            item.ApplyTemplate();
            if (item.Template.FindName("PART_Popup", item) is not Popup popup) return;
            popup.PlacementTarget = item; popup.Placement = PlacementMode.Custom;
            popup.CustomPopupPlacementCallback = (size, targetSize, offset) => item.Role is MenuItemRole.TopLevelHeader or MenuItemRole.TopLevelItem
                ? Below(item, size, targetSize, offset)
                : new[] { new CustomPopupPlacement(new Point(targetSize.Width, 0), PopupPrimaryAxis.Vertical), new CustomPopupPlacement(new Point(-size.Width, 0), PopupPrimaryAxis.Vertical) };
        };
    }
    public static CustomPopupPlacement[] Below(FrameworkElement anchor, Size size, Size target, Point offset)
    {
        double x = 0;
        if (Window.GetWindow(anchor) is { } window)
        {
            double left = anchor.TranslatePoint(new Point(), window).X;
            x = Math.Clamp(left, 0, Math.Max(0, window.ActualWidth - size.Width)) - left;
        }
        return new[]
        {
            new CustomPopupPlacement(new Point(x, target.Height), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(x, -size.Height), PopupPrimaryAxis.Horizontal)
        };
    }
}
