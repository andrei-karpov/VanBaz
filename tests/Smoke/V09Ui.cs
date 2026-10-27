using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VanBaz;

internal static class V09Ui
{
    public static void Render(string folder)
    {
        string? output = Environment.GetEnvironmentVariable("VANBAZ_UI_ARTIFACTS"); if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        var context = new ProfileContext { Roaming = Path.Combine(folder, "Roaming"), Local = Path.Combine(folder, "Local"), BaseId = "11111111-1111-1111-1111-111111111111", UserId = "22222222-2222-2222-2222-222222222222" };
        var demo = new BaseEntry { Name = "Учебная база", Location = @"D:\Учебные базы\Бухгалтерия", Id = context.BaseId, Version = "8.3", UserName = "Тестовый пользователь" };
        var profile = LaunchProfile.From(demo, false, "Толстый управляемый"); profile.Name = "Разработка";
        var plan = new ReconcilePlan { Path = @"C:\Тестовый профиль\AppData\Roaming\1C\1CEStart\ibases.v8i", Rows = new()
        {
            new() { Id = "one", Local = new() { Name = "Бухгалтерия — локальное имя", Id = "one", Fields = new() { ["Folder"] = "/Клиенты", ["Version"] = "8.3" } }, External = new() { Name = "Бухгалтерия — внешнее имя", Id = "one", Fields = new() { ["Folder"] = "/Клиенты", ["Version"] = "8.3" } }, Change = "Конфликт: изменены обе стороны" },
            new() { Id = "two", External = new() { Name = "Новая регистрация", Id = "two", Fields = new() { ["Version"] = "8.3" } }, Change = "Новая запись в файле", Choice = CatalogReconciler.Choices[1] }
        }};
        plan.Rows[0].Local!.Fields["Connect"] = "File=\"D:\\Локальная база\";Pwd=\"synthetic-hidden\";";
        plan.Rows[0].External!.Fields["Connect"] = "File=\"D:\\Внешняя база\";Pwd=\"synthetic-hidden\";";
        foreach (string theme in new[] { "Светло-серый", "Тёмный" })
        {
            ThemeService.Apply(new Preferences { Theme = theme }); string suffix = theme == "Тёмный" ? "dark" : "light";
            void Capture(Window window, string name, int width, int height)
            {
                try
                {
                    var content = (FrameworkElement)window.Content;
                    content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                    content.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded); content.UpdateLayout();
                    var background = new DrawingVisual(); using (var dc = background.RenderOpen()) dc.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, width, height));
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(background); bitmap.Render(content);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, name + "-" + suffix + ".png")); png.Save(file);
                    if (!double.IsFinite(content.ActualWidth) || content.ActualWidth <= 0 || content.ActualWidth > width || content.ActualHeight > height) throw new Exception("Unexpected layout dimensions");
                }
                finally { window.Close(); }
            }
            Capture(new ReconcileWindow(plan), "reconcile", 1020, 600);
            Capture(new ReconcileWindow(new ReconcilePlan { Path = plan.Path, Rows = plan.Rows, CanWriteSource = false }), "reconcile-readonly", 1020, 600);
            Capture(new LaunchOptionsDialog(demo, profile, Array.Empty<PlatformInfo>(), true), "launch-profile", 820, 580);
            var advanced = new LaunchOptionsDialog(demo, profile, Array.Empty<PlatformInfo>(), true); advanced.SelectSection(2); Capture(advanced, "launch-advanced", 820, 580);
            var files = new ProfileFilesWindow(context, Path.Combine(folder, "recovery"));
            ((System.Windows.Controls.DockPanel)files.Content).Children.OfType<System.Windows.Controls.DataGrid>().Single().SelectedIndex = 10;
            Capture(files, "profile-files", 1030, 550);
            demo.ListSources = new() { @"D:\Списки\Команда.v8i", @"C:\Тестовый профиль\AppData\Roaming\1C\1CEStart\ibases.v8i" };
            var editor = new BaseEditor(demo.Copy(), new(), new()); editor.SelectSection(3); Capture(editor, "base-sources", 780, 570);
        }
    }
}
