using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VanBaz;
public static class AppBrand
{
    private static readonly Uri IconUri = new("pack://application:,,,/VanBaz;component/Assets/VanBaz.ico");
    public static ImageSource Image { get; } = LoadImage();
    public static System.Drawing.Icon TrayIcon { get; } = LoadIcon();
    private static ImageSource LoadImage()
    {
        var image = BitmapFrame.Create(IconUri); image.Freeze(); return image;
    }
    private static System.Drawing.Icon LoadIcon()
    {
        using var stream = System.Windows.Application.GetResourceStream(IconUri).Stream;
        using var icon = new System.Drawing.Icon(stream); return (System.Drawing.Icon)icon.Clone();
    }
}
