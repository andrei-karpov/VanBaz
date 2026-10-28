using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public sealed class GroupSelectionWindow : Window
{
    public TreeView Groups { get; } = new() { Name = "GroupTree" };
    public TextBox PathBox { get; } = new() { Name = "GroupPath" };
    public Button SelectButton { get; } = new() { Name = "SelectGroup", Content = "Выбрать", IsDefault = true };
    public string SelectedGroup => Normalize(PathBox.Text);
    private readonly Dictionary<string, TreeViewItem> nodes = new(StringComparer.OrdinalIgnoreCase);

    public GroupSelectionWindow(IEnumerable<string> paths, string selected, string caption = "Выбор группы")
    {
        Title = caption; Width = 520; Height = 470; MinWidth = 390; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; DialogBehavior.FitToScreen(this);
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) }; DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        var expand = new Button { Name = "ExpandGroups", Content = UiIcons.Label("tree-open", "Развернуть"), ToolTip = "Развернуть все группы" };
        var collapse = new Button { Name = "CollapseGroups", Content = UiIcons.Label("tree-closed", "Свернуть"), ToolTip = "Свернуть вложенные группы" };
        tools.Children.Add(expand); tools.Children.Add(collapse);
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(new TextBlock { Text = "Путь группы", Margin = new Thickness(0, 0, 0, 4) }); footer.Children.Add(PathBox);
        System.Windows.Automation.AutomationProperties.SetName(PathBox, "Путь группы");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMuted"); footer.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отмена", IsCancel = true }; cancel.Click += (_, _) => Close();
        buttons.Children.Add(SelectButton); buttons.Children.Add(cancel); footer.Children.Add(buttons);
        Groups.SetResourceReference(Control.BackgroundProperty, "ThemeSurface"); Groups.SetResourceReference(Control.ForegroundProperty, "ThemeText");
        Groups.SetResourceReference(Control.BorderBrushProperty, "ThemeBorder"); root.Children.Add(Groups);
        var top = new TreeViewItem { Header = UiIcons.Label("folder-open", "Корень списка"), Tag = "", IsExpanded = true };
        nodes.Add("", top); Groups.Items.Add(top);
        foreach (string path in paths.Append(selected).Select(Normalize).Where(p => p.Length > 0 && Valid(p)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase))
        {
            string current = "";
            foreach (string part in path.Split('/'))
            {
                string parent = current; current = parent.Length == 0 ? part : parent + "/" + part;
                if (nodes.ContainsKey(current)) continue;
                var item = new TreeViewItem { Header = UiIcons.Label("folder", part), Tag = current, IsExpanded = true, Padding = new Thickness(2, 3, 2, 3) };
                nodes.Add(current, item); nodes[parent].Items.Add(item);
            }
        }
        Groups.SelectedItemChanged += (_, _) => { if (Groups.SelectedItem is TreeViewItem { Tag: string path }) PathBox.Text = path; };
        PathBox.TextChanged += (_, _) => Refresh();
        PathBox.Text = Normalize(selected);
        if (nodes.TryGetValue(SelectedGroup, out var initial)) initial.IsSelected = true;
        expand.Click += (_, _) => { foreach (var node in nodes.Values) node.IsExpanded = true; };
        collapse.Click += (_, _) => { foreach (var pair in nodes) pair.Value.IsExpanded = pair.Key.Length == 0; };
        SelectButton.Click += (_, _) => { if (Valid(SelectedGroup)) DialogResult = true; };
        Loaded += (_, _) => { (Groups.SelectedItem as TreeViewItem)?.BringIntoView(); Groups.Focus(); };
        Refresh();

        void Refresh()
        {
            SelectButton.IsEnabled = Valid(SelectedGroup);
            status.Text = !SelectButton.IsEnabled ? "В пути не должно быть пустых частей, перевода строки или знака ]."
                : SelectedGroup.Length == 0 ? "Выбран корень списка."
                : nodes.ContainsKey(SelectedGroup) ? "Выбрана группа: " + SelectedGroup
                : "Будет использован новый путь группы. Вложенные группы разделяются знаком /.";
        }
    }

    private static string Normalize(string value) => value.Trim().Replace('\\', '/').Trim('/');
    private static bool Valid(string value) => value.Length == 0 || value.IndexOfAny(new[] { '\r', '\n', ']' }) < 0 && value.Split('/').All(p => !string.IsNullOrWhiteSpace(p));
}
