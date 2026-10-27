namespace VanBaz;

// Physical screen pixels, never WPF virtual-screen DIPs or WINDOWPLACEMENT workspace coordinates.
public record ScreenArea(string Device, int Left, int Top, int Width, int Height, double Dpi = 96);
public record PixelBounds(int Left, int Top, int Width, int Height);
public sealed record SavedWindowPlacement(ScreenArea Screen, PixelBounds Bounds);

public static class WindowGeometry
{
    public static bool Valid(SavedWindowPlacement? p) => p is { Screen.Width: > 0, Screen.Height: > 0, Bounds.Width: > 0, Bounds.Height: > 0 }
        && double.IsFinite(p.Screen.Dpi) && p.Screen.Dpi is >= 48 and <= 960;

    public static (ScreenArea Screen, PixelBounds Bounds) Fit(SavedWindowPlacement saved, IReadOnlyList<ScreenArea> screens)
    {
        if (screens.Count == 0 || !Valid(saved)) throw new ArgumentException("Недопустимое положение окна или список мониторов.");
        var target = screens.FirstOrDefault(s => s.Device == saved.Screen.Device) ?? screens.MinBy(s => Distance(saved.Bounds, s))!;
        double scale = target.Dpi / saved.Screen.Dpi;
        int width = (int)Math.Clamp(Math.Round(saved.Bounds.Width * scale), 1, target.Width);
        int height = (int)Math.Clamp(Math.Round(saved.Bounds.Height * scale), 1, target.Height);
        int x = (int)Math.Clamp(target.Left + Math.Round(((double)saved.Bounds.Left - saved.Screen.Left) * scale), target.Left, (double)target.Left + target.Width - width);
        int y = (int)Math.Clamp(target.Top + Math.Round(((double)saved.Bounds.Top - saved.Screen.Top) * scale), target.Top, (double)target.Top + target.Height - height);
        return (target, new(x, y, width, height));
    }
    private static double Distance(PixelBounds b, ScreenArea s)
    {
        double x = b.Left + b.Width / 2d, y = b.Top + b.Height / 2d;
        double dx = x - Math.Clamp(x, s.Left, (double)s.Left + s.Width), dy = y - Math.Clamp(y, s.Top, (double)s.Top + s.Height);
        return dx * dx + dy * dy;
    }
}
