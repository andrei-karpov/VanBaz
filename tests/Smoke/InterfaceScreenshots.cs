using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VanBaz;

internal static class InterfaceScreenshots
{
    // Actual WPF controls with fictional data, kept outside the shipped application.
    public static void Run(string output)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(output);
                var app = UiRegression.CreateApplication();
                var catalog = new Catalog { FeatureMigrationVersion = FeatureMigration.CurrentVersion };
                catalog.Settings.InterfaceMode = "Разработчик"; catalog.Settings.HotkeysEnabled = false;
                catalog.Settings.CloseToTray = false; catalog.Settings.CheckExistence = false;
                catalog.Settings.ShowDate = false; catalog.Settings.ShowSize = false;
                catalog.Settings.FontName = "Segoe UI"; catalog.Settings.FontSize = 13; catalog.Settings.RowHeight = 27;
                catalog.Groups.AddRange(new[] { new BaseGroup { Path = "РАБОЧИЕ БАЗЫ" }, new BaseGroup { Path = "РАЗРАБОТКА" } });
                catalog.Bases.AddRange(new[] {
                    new BaseEntry { Id="11111111-1111-1111-1111-111111111111", Name="Бухгалтерия предприятия 3.0", Group="РАБОЧИЕ БАЗЫ", Location=@"D:\Учебные базы\Бухгалтерия", Favorite=true, Notes="Учебная база для проверки настроек и отчётов" },
                    new BaseEntry { Name="Зарплата и управление персоналом 3.1", Group="РАБОЧИЕ БАЗЫ", Location=@"D:\Учебные базы\Зарплата", Favorite=true },
                    new BaseEntry { Name="Управление торговлей 11", Group="РАБОЧИЕ БАЗЫ", Kind="Server", Location="training-server/trade" },
                    new BaseEntry { Name="Комплексная автоматизация 2", Group="РАБОЧИЕ БАЗЫ", Kind="Server", Location="training-server/automation" },
                    new BaseEntry { Name="ERP: учебная копия", Group="РАБОЧИЕ БАЗЫ", Kind="Server", Location="training-server/erp" },
                    new BaseEntry { Name="Разработка расширений", Group="РАЗРАБОТКА", Location=@"D:\Учебные базы\Расширения", Version="8.5" },
                    new BaseEntry { Name="Проверка обновления", Group="РАЗРАБОТКА", Location=@"D:\Учебные базы\Обновление" },
                    new BaseEntry { Name="Веб-публикация", Group="РАЗРАБОТКА", Kind="Web", Location="https://example.invalid/training" }
                });
                catalog.Settings.SelectedId = catalog.Bases[0].Id;
                string data = Path.Combine(Path.GetTempPath(), "VanBaz-screenshots-" + Guid.NewGuid().ToString("N"));
                var store = new CatalogStore(data); store.Save(catalog);
                foreach (string theme in new[] { "Светло-серый", "Тёмный" })
                {
                    catalog.Settings.Theme = theme; ThemeService.Apply(catalog.Settings);
                    var window = new MainWindow(store, catalog, true) { Width=920, Height=700 };
                    window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture(window, Path.Combine(output, theme == "Тёмный" ? "main-dark.png" : "main-light.png"));
                    if (theme == "Светло-серый")
                    {
                        var themes = (FormDialog)typeof(MainWindow).GetMethod("CreateThemesDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window,new object[]{window})!;
                        themes.Show(); themes.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        Capture(themes, Path.Combine(output,"themes.png")); themes.Close();
                    }
                    window.Close();
                }
                app.Shutdown();
                Console.WriteLine("Saved three WPF interface images with fictional bases.");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Exception("Interface capture failed", failure);
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*2), (int)Math.Ceiling(content.ActualHeight*2), 192,192,PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            drawing.DrawRectangle(window.Background, null, bounds);
            drawing.DrawRectangle(new VisualBrush(content), null, bounds);
        }
        bitmap.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path); png.Save(file);
    }
}
