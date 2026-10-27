using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VanBaz;

internal static class V15Ui
{
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    public static void Run(MainWindow main, Catalog catalog, string scratch)
    {
        void Require(bool ok, string why) { if (!ok) throw new Exception("v15: " + why); }
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, args);
        var fieldsInfo = typeof(FormDialog).GetField("fields", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Dictionary<string, Control> Fields(FormDialog f) => (Dictionary<string, Control>)fieldsInfo.GetValue(f)!;
        var grid = (DataGrid)main.FindName("BasesGrid"); ((RadioButton)main.FindName("BasesTab")).IsChecked = true;
        new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
        grid.SelectedItem = grid.Items.Cast<ListRow>().First(r => r.Entry?.Id == "file");
        var summary = (BaseInfoWindow)Call("CreateSummaryWindow")!;
        Require(summary.Summary.Contains("File") && summary.Summary.Contains("Идентификатор"), "base summary window includes displayed fields");
        string copied = ""; var copyFixture = new BaseInfoWindow("Copy fixture",new[]{new InfoField("Наименование","Fixture"),new InfoField("ID","fixture-id")},text=>copied=text);
        copyFixture.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(copied == copyFixture.Summary,"Copy all routes the complete summary to clipboard writer"); copyFixture.Close();
        grid.SelectedItem = grid.Items.Cast<ListRow>().First(r => r.GroupKey == "group:Clients");
        var group = (BaseInfoWindow)Call("CreateSummaryWindow")!; Require(group.Summary.Contains("Баз в группе: 1"), "group summary available");
        Require(SessionLockPanel.BeginPresets.SequenceEqual(new[] {0,5,10,15,20,25,30,45,50,60,120,180}) && SessionLockPanel.EndPresets.SequenceEqual(new[] {5,10,15,20,25,30,60,120,180}), "complete lock date presets");
        var entry = new BaseEntry { Name = "Server fixture", Kind = "Server", Location = "host:1541/erp" };
        var editor = new BaseEditor(entry, new(), new()); var fields = Fields(editor);
        Require(((TextBox)fields["server"]).Text == "host:1541" && ((TextBox)fields["reference"]).Text == "erp" && ((FrameworkElement)fields["location"].Parent).Visibility == Visibility.Collapsed, "server has separate address fields and no folder picker");
        ((TextBox)fields["reference"]).Text = "changed"; Require(editor.Validate!() && editor.Result.Location == "host:1541/changed", "edited server fields reach model");
        ((ComboBox)fields["kind"]).SelectedItem = "Веб-публикация"; ((TextBox)fields["url"]).Text = "https://example.invalid/base";
        Require(editor.Validate!() && editor.Result.Kind == "Web" && editor.Result.Location.EndsWith("/base"), "Web URL reaches model");
        editor.UseCommonSource(); Require(!fields["url"].IsEnabled && fields["group"].IsEnabled, "common connection remains read only while placement is editable"); editor.Close();
        var naming = new BaseEditor(new BaseEntry(), new(), new()); naming.UseNewNamePolicy(new() { NewBaseNameFromFolder = true, NewBaseNameWithDate = true });
        var nf = Fields(naming); ((TextBox)nf["location"]).Text = @"D:\Fixture\Accounting";
        Require(((TextBox)nf["name"]).Text.StartsWith("Accounting "), "name preferences are wired to folder edits"); ((TextBox)nf["name"]).Text = "Explicit"; ((TextBox)nf["location"]).Text = @"D:\Fixture\Other";
        Require(((TextBox)nf["name"]).Text == "Explicit", "generated name does not overwrite manual input"); naming.Close();
        int applies = 0; var form = new FormDialog("Apply fixture", 550, 330); form.Section("Параметры"); var box = form.Text("text", "Текст", "one"); string saved = "";
        form.ApplyChanges = () => { applies++; saved = form.Value("text"); return true; }; form.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        form.Measure(new Size(550,330)); form.Arrange(new Rect(0,0,550,330)); form.UpdateLayout();
        var apply = Descendants<Button>((DependencyObject)form.Content).Single(b => b.Content?.ToString() == "Применить"); apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); box.Text = "unsaved"; form.Close();
        Require(applies == 1 && saved == "one", "Apply saves without closing and later edits do not change saved value");
        string? output = Environment.GetEnvironmentVariable("VANBAZ_UI_ARTIFACTS"); int renders = 0;
        void Capture(FrameworkElement element, Brush? background, string file, int width, int height, double scale = 1)
        {
            element.Measure(new Size(width,height)); element.Arrange(new Rect(0,0,width,height)); element.UpdateLayout();
            element.Dispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.Loaded); element.UpdateLayout();
            if (output == null) return;
            Directory.CreateDirectory(output); var backdrop = new DrawingVisual(); using (var draw = backdrop.RenderOpen()) draw.DrawRectangle(background ?? Brushes.White, null, new Rect(0,0,width,height));
            var bmp = new RenderTargetBitmap((int)(width*scale),(int)(height*scale),96*scale,96*scale,PixelFormats.Pbgra32); bmp.Render(backdrop); bmp.Render(element);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using var stream = File.Create(Path.Combine(output,file+".png")); png.Save(stream); renders++;
        }
        foreach (var theme in new[] { "Светло-серый", "Тёмный" })
        {
            catalog.Settings.Theme = theme; Call("ApplyPreferences", false); string suffix = theme == "Тёмный" ? "dark" : "light";
            grid.SelectedItem = grid.Items.Cast<ListRow>().First(r => r.Entry?.Id == "file");
            double? toolbarLeft = null;
            foreach (var size in new[] { (717,590,1d,"normal"), (1920,1000,1d,"maximized"), (1280,680,1.5,"maximized-150"), (960,600,2d,"maximized-200") })
            {
                Capture((FrameworkElement)main.Content,main.Background,"v15-"+suffix+"-"+size.Item4,size.Item1,size.Item2,size.Item3);
                var toolbar = (FrameworkElement)main.FindName("BaseToolbar"); double x = toolbar.TranslatePoint(new Point(), (UIElement)main.Content).X;
                toolbarLeft ??= x; Require(Math.Abs(x-toolbarLeft.Value)<.1 && x<200, "toolbar remains anchored beside fixed search on maximize");
                var icons = Descendants<Image>((DependencyObject)main.Content).Where(i => i.IsVisible && i.ActualWidth > 0).ToList();
                // Unshown windows report IsVisible=false; size/arrange still exercises actual templates.
                if (icons.Count == 0) icons = Descendants<Image>((DependencyObject)main.Content).Where(i => i.Visibility == Visibility.Visible && i.ActualWidth > 0).ToList();
                Require(icons.Count > 8 && icons.All(i => Math.Abs(i.ActualWidth-16)<.05 && Math.Abs(i.ActualHeight-16)<.05), "icons retain 16 DIP square at " + size.Item4 + "; " + string.Join(",", icons.Select(i=>$"{i.ActualWidth}x{i.ActualHeight}")));
                foreach (var image in icons)
                { var position = image.TranslatePoint(new Point(), (UIElement)main.Content); Require(position.X >= -1 && position.Y >= -1 && position.X + 16 <= size.Item1 + 1 && position.Y + 16 <= size.Item2 + 1, "icons remain inside window at " + size.Item4); }
            }
            Capture((FrameworkElement)summary.Content,main.Background,"v15-summary-"+suffix,564,360); Capture((FrameworkElement)group.Content,main.Background,"v15-group-summary-"+suffix,564,360);
            var settings = (FormDialog)Call("CreateSettingsDialog")!; settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var navigation = (ListBox)typeof(FormDialog).GetField("navigation",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(settings)!;
            for(int section=0;section<navigation.Items.Count;section++) { settings.SelectSection(section); Capture((FrameworkElement)settings.Content,main.Background,$"v15-settings-{section}-{suffix}",764,581); }
            var sf = Fields(settings); ((ComboBox)sf["proxymode"]).SelectedItem = "Без прокси"; Require(!sf["proxy"].IsEnabled,"disabled proxy fields follow mode");
            ((ComboBox)sf["proxymode"]).SelectedItem = "Указать прокси"; Require(sf["proxy"].IsEnabled,"manual proxy enables credentials"); settings.Close();
            var command = (FormDialog)Call("CreateCommandDialog",new CommandPreset())!; command.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); var cf = Fields(command);
            foreach(string kind in new[]{"Программа","Каталог","Документ","Скрипт","Web-адрес","Параметры 1С"})
            { ((ComboBox)cf["kind"]).SelectedItem = kind; Capture((FrameworkElement)command.Content,main.Background,"v15-command-"+Array.IndexOf(new[]{"Программа","Каталог","Документ","Скрипт","Web-адрес","Параметры 1С"},kind)+"-"+suffix,684,451); }
            Require(!cf["target"].IsEnabled && cf["args"].IsEnabled,"1C parameter presets remain distinct from standalone commands"); command.Close();
            foreach (string kind in new[] { "File", "Server", "Web" })
            {
                var b = new BaseEditor(new BaseEntry { Name = "Тестовая база", Kind = kind, Location = kind == "File" ? @"D:\Fixture\Base" : kind == "Server" ? "server:1541/base" : "https://example.invalid/base" }, new(), new() {"Клиенты"});
                for (int section = 0; section < 5; section++) { b.SelectSection(section); Capture((FrameworkElement)b.Content,main.Background,$"v15-properties-{kind}-{section}-{suffix}",674,501); }
                b.Close();
            }
            var search = new BaseSearchWindow(catalog,"Clients"); search.Results.Add(new() { Name = "Тестовая база", Folder = @"D:\Fixture\Base", Modified = new DateTime(2026,10,24), Bytes = 450000000 });
            Capture((FrameworkElement)search.Content,main.Background,"v15-search-"+suffix,834,591); search.Close();
            var platforms = new PlatformsWindow(new[] { new PlatformInfo("8.3.27.1", @"C:\Fixture\8.3\bin", "x64") }); platforms.Grid.SelectedIndex = 0;
            Capture((FrameworkElement)platforms.Content,main.Background,"v15-platforms-"+suffix,744,421); platforms.Close();
        }
        catalog.Settings.FontName = "Calibri"; catalog.Settings.FontSize = 14d*96/72; catalog.Settings.RowHeight=29; Call("ApplyPreferences",false);
        var largeRow = grid.Items.Cast<ListRow>().First(r=>r.Entry?.Id=="file"); largeRow.Entry!.Version="8.3.27.1719"; largeRow.Date="24.10.26"; largeRow.Size="999,99 ГБ"; grid.Items.Refresh(); Call("ApplyColumnWidths");
        Require(((DataGridColumn)main.FindName("DateColumn")).Width.Value > 70 && ((DataGridColumn)main.FindName("SizeColumn")).Width.Value > 65, "default widths grow to fit imported font, while saved widths are preserved");
        Capture((FrameworkElement)main.Content,main.Background,"v15-imported-font-fullscreen",1920,1000);
        summary.Close(); group.Close(); Console.WriteLine($"PASS v15 UI: connection types, common permissions, default names, Apply, summary, lock presets, icon geometry; {renders} renders.");
    }
}
