using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VanBaz;

internal static class V14Ui
{
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    static void Require(bool condition, string text) { if (!condition) throw new Exception(text); }
    public static void Run(MainWindow main, Catalog catalog)
    {
        var state = new SessionLockState { Enabled = false, Begin = SessionLockPanel.EmptyDate, End = SessionLockPanel.EmptyDate, Message = "Начальное сообщение", Parameter = "opaque-parameter" };
        int reads = 0, writes = 0; bool mismatch = false;
        var entry = catalog.Bases.First(b => b.Kind == "File");
        var panel = new SessionLockPanel(entry.Copy(), true, (op, arch, code, next, expected) =>
        {
            if (op == "get-lock") reads++;
            else
            {
                writes++;
                Require(JsonSerializer.Serialize(state) == JsonSerializer.Serialize(expected), "Lock write must include the exact observed state");
                if (!mismatch) state = JsonSerializer.Deserialize<SessionLockState>(JsonSerializer.Serialize(next))!;
            }
            return Task.FromResult(new BridgeResult { Ok = true, Lock = state });
        });
        panel.ApplyAsync().GetAwaiter().GetResult(); Require(writes == 0, "Cannot write before reading");
        panel.ReadAsync().GetAwaiter().GetResult();
        Field<CheckBox>(panel,"enabled").IsChecked = true;
        Field<TextBox>(panel,"key").Text = "";
        var value = panel.ReadValue();
        Require(value.Enabled && value.Begin == SessionLockPanel.EmptyDate && value.End == SessionLockPanel.EmptyDate && value.KeyCode == "" && value.Parameter == "opaque-parameter", "Optional dates, blank code and opaque platform parameter must survive editing");
        panel.ApplyAsync().GetAwaiter().GetResult(); Require(writes == 1 && state.Enabled, "Lock applies after explicit read");
        Field<CheckBox>(panel,"useBegin").IsChecked = true; Field<DatePicker>(panel,"beginDate").SelectedDate = DateTime.Today.AddDays(1); Field<TextBox>(panel,"beginTime").Text = "12:30:00";
        Field<CheckBox>(panel,"useEnd").IsChecked = true; Field<DatePicker>(panel,"endDate").SelectedDate = DateTime.Today.AddDays(1); Field<TextBox>(panel,"endTime").Text = "12:00:00";
        panel.ApplyAsync().GetAwaiter().GetResult(); Require(writes == 1, "Invalid interval must not reach platform");
        Field<TextBox>(panel,"endTime").Text = "13:00:00"; mismatch = true;
        panel.ApplyAsync().GetAwaiter().GetResult(); panel.ApplyAsync().GetAwaiter().GetResult();
        Require(writes == 2 && !Field<Button>(panel,"apply").IsEnabled, "Failed readback must require reread and disable repeated writes");
        mismatch = false; panel.ReadAsync().GetAwaiter().GetResult();
        Field<PasswordBox>(panel,"currentCode").Password = "changed";
        Require(!Field<Button>(panel,"apply").IsEnabled && reads == 2, "Changing credentials invalidates observed state");
        panel.ReadAsync().GetAwaiter().GetResult();
        var service = (MenuItem)main.FindName("ServiceMenu");
        Require(!service.Items.OfType<MenuItem>().Any(m => m.Header?.ToString()?.Contains("Резервное") == true) && main.FindName("BackupButton") == null, "Removed backup must have no main button or menu");
        typeof(MainWindow).GetMethod("AddMenuIcons", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[]{service});
        Require(service.Items.OfType<MenuItem>().All(m=>m.Icon is Image), "Service menu icons must be present");
        var grid = (DataGrid)main.FindName("BasesGrid");
        ((RadioButton)main.FindName("BasesTab")).IsChecked = true;
        grid.SelectedItem = grid.Items.Cast<ListRow>().First(r=>r.Entry?.Id == entry.Id);
        var link = (Button)main.FindName("ConnectionLink");
        Require(link.Visibility == Visibility.Visible && ((TextBox)main.FindName("SelectedConnection")).Visibility == Visibility.Collapsed, "File connection uses folder link");
        var menu = (ContextMenu)typeof(MainWindow).GetMethod("BaseActions",BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main,null)!;
        typeof(MainWindow).GetMethod("AddMenuIcons",BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,new object[]{menu});
        var iconItem = menu.Items.OfType<MenuItem>().First(); iconItem.ApplyTemplate();
        Require(iconItem.Template.FindName("IconPresenter",iconItem) is ContentPresenter, "Menu template must display actual icons");
        var summary = MainWindow.SummaryText(new BaseEntry { Name="Summary", Description="Comment", Notes="Note", Location=@"D:\Demo", EnterpriseSecret="secret-sentinel" });
        Require(summary.Contains("Summary") && summary.Contains("Comment") && summary.Contains("Note") && summary.Contains("File=") && !summary.Contains("secret-sentinel"), "Summary includes shown details and excludes secrets");
        string? output = Environment.GetEnvironmentVariable("VANBAZ_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        void Capture(FrameworkElement content, Brush background, string name, int width, int height)
        {
            content.Measure(new Size(width,height)); content.Arrange(new Rect(0,0,width,height)); content.UpdateLayout();
            content.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Loaded); content.UpdateLayout();
            var backdrop = new DrawingVisual(); using(var drawing=backdrop.RenderOpen()) drawing.DrawRectangle(background,null,new Rect(0,0,width,height));
            var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(backdrop); bitmap.Render(content);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream=File.Create(Path.Combine(output,name+".png")); png.Save(stream);
        }
        var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (string theme in new[]{"Светло-серый","Тёмный"})
        {
            catalog.Settings.Theme=theme;
            typeof(MainWindow).GetMethod("ApplyPreferences",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,new object[]{false});
            grid.SelectedItem=grid.Items.Cast<ListRow>().First(r=>r.Entry?.Id==entry.Id);
            string suffix=theme=="Тёмный"?"dark":"light";
            Capture((FrameworkElement)main.Content,main.Background,"v14-main-"+suffix,717,590);
            link.SetValue(hoverKey,true); link.ApplyTemplate();
            var text=(TextBlock)link.Template.FindName("LinkText",link);
            Require(text.TextDecorations.Any(d => d.Location == TextDecorationLocation.Underline), "Folder link must underline on hover; hover=" + link.IsMouseOver + "; decorations=" + text.TextDecorations.Count);
            var toolbar=(StackPanel)main.FindName("BaseToolbar"); var tool=toolbar.Children.OfType<Button>().First(); tool.SetValue(hoverKey,true);
            Capture((FrameworkElement)main.Content,main.Background,"v14-hover-"+suffix,717,590);
            link.SetValue(hoverKey,false); tool.SetValue(hoverKey,false);
            menu = (ContextMenu)typeof(MainWindow).GetMethod("BaseActions",BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main,null)!;
            typeof(MainWindow).GetMethod("AddMenuIcons",BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,new object[]{menu});
            Capture(menu,(Brush)Application.Current.FindResource("ThemeSurface"),"v14-context-"+suffix,330,440);
            var unlock=new ApplicationLockWindow(); unlock.Transparency.Value=130; Require(Math.Abs(unlock.Opacity-130d/255)<0.001,"Unlock opacity tracks slider"); unlock.Transparency.Value=255;
            Capture((FrameworkElement)unlock.Content,unlock.Background,"v14-app-lock-"+suffix,369,151); unlock.Close();
            var dialog=new BaseEditor(entry.Copy(),new(),new()); dialog.SelectSection(4);
            Capture((FrameworkElement)dialog.Content,dialog.Background,"v14-properties-lock-"+suffix,674,501); dialog.Close();
            var host=new Window { Style=(Style)Application.Current.FindResource(typeof(Window)), Content=panel, Padding=new(12) };
            Field<CheckBox>(panel,"enabled").IsChecked=true;
            Capture(panel,host.Background,"v14-lock-loaded-"+suffix,460,390); host.Content=null; host.Close();
        }
        grid.SelectedItem=grid.Items.Cast<ListRow>().First(r=>r.Entry?.Kind=="Web");
        Require(link.Visibility==Visibility.Collapsed && ((TextBox)main.FindName("SelectedConnection")).IsReadOnly,"Web connection remains selectable text, not folder action");
        Console.WriteLine("PASS v14 UI: backup removed, menu icons, folder hover, summary, shared lock read/write/validation/readback; 12 renders.");
    }
}
