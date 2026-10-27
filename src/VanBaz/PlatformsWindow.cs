using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace VanBaz;

public sealed class PlatformsWindow : Window
{
    public DataGrid Grid { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
    public PlatformsWindow(IEnumerable<PlatformInfo> platforms)
    {
        Title = "Установленные платформы 1С"; Width = 760; Height = 460; MinWidth = 560; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; UseLayoutRounding = true;
        var root = new DockPanel { Margin = new(10) }; Content = root;
        var source = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(4), Text = "Выберите установленную платформу." }; DockPanel.SetDock(source,Dock.Top); root.Children.Add(source);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        Button Action(string caption, string icon, Action action)
        { var b = new Button { Content = UiIcons.Label(icon, caption) }; b.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); } }; footer.Children.Add(b); return b; }
        var open = Action("Папка", "folder", () => { if (Grid.SelectedItem is PlatformInfo p) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { p.Directory } }); });
        var copy = Action("Копировать путь", "copy", () => { });
        copy.Click += async (_, _) => { if (Grid.SelectedItem is PlatformInfo p) await ClipboardService.CopyAsync(this, p.Directory, () => source.Text = "Путь скопирован.", error => source.Text = error); };
        Action("Установить…", "+", () => { var file = new OpenFileDialog { Title = "Установщик платформы 1С", Filter = "Установщик|setup.exe;*.msi" }; if (file.ShowDialog(this) == true) Process.Start(new ProcessStartInfo(file.FileName) { UseShellExecute = true }); });
        var uninstall = Action("Деинсталлировать", "×", () => { if (Grid.SelectedItem is PlatformInfo p && PlatformInstallation.Find(p) is { } installation && MessageBox.Show(this,"Удалить платформу " + p.Display + "?\n" + p.Directory,"Удаление платформы",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes) Process.Start(installation.Uninstall()); });
        Action("Свернуть всё", "⊟", () => Expand(false)); Action("Развернуть всё", "⊞", () => Expand(true));
        Action("Закрыть", "×", Close);
        DialogBehavior.Selection(Grid, open, copy);
        foreach (var c in new[] { ("Версия", "Version", 130d), ("Разрядность", "Architecture", 85d), ("Каталог", "Directory", 350d) })
            Grid.Columns.Add(new DataGridTextColumn { Header = c.Item1, Binding = new Binding(c.Item2), Width = c.Item2 == "Directory" ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(c.Item3) });
        var data = platforms.OrderByDescending(p => Version.TryParse(p.Version, out var v) ? v : new Version()).ThenBy(p => p.Architecture).ToList();
        var view = new ListCollectionView(data); view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PlatformInfo.Family))); Grid.ItemsSource = view;
        var groupStyle = new GroupStyle(); var style = new Style(typeof(GroupItem)); var expander = new FrameworkElementFactory(typeof(Expander)); expander.SetValue(Expander.IsExpandedProperty,true); expander.SetBinding(Expander.HeaderProperty,new Binding("Name")); expander.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter))); style.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(GroupItem)) { VisualTree = expander })); groupStyle.ContainerStyle=style; Grid.GroupStyle.Add(groupStyle);
        uninstall.IsEnabled=false;
        Grid.SelectionChanged += async (_, _) =>
        {
            uninstall.IsEnabled=false;
            if(Grid.SelectedItem is not PlatformInfo p) { source.Text="Установлено версий: "+data.Count; return; }
            try { var found=await Task.Run(()=>PlatformInstallation.Find(p)); if(!ReferenceEquals(Grid.SelectedItem,p)) return; uninstall.IsEnabled=found!=null; source.Text="Установлено версий: "+data.Count+". Источник установки: "+(found?.Source.Length>0 ? found.Source : "не зарегистрирован"); }
            catch(Exception ex) { if(ReferenceEquals(Grid.SelectedItem,p)) source.Text="Не удалось прочитать сведения установщика: "+ex.Message; }
        };
        var context = new ContextMenu(); Grid.ContextMenu = context;
        foreach (var pair in new[] { ("Открыть каталог", "folder", open), ("Копировать путь", "copy", copy) })
        { var item = new MenuItem { Header = pair.Item1, Icon = UiIcons.Icon(pair.Item2) }; item.Click += (_, _) => pair.Item3.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); context.Items.Add(item); }
        context.Opened += (_, _) => { foreach (MenuItem item in context.Items) item.IsEnabled = Grid.SelectedItem != null; };
        Grid.MouseDoubleClick += (_, e) => { if (DialogBehavior.OnItem(e.OriginalSource)) open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        root.Children.Add(Grid); DialogBehavior.CloseOnEscape(this);
    }
    private void Expand(bool value)
    {
        void Visit(DependencyObject node) { for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++) { var child=System.Windows.Media.VisualTreeHelper.GetChild(node,i); if(child is Expander e) e.IsExpanded=value; Visit(child); } }
        Visit(Grid);
    }
}
