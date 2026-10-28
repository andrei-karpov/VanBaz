using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VanBaz;

public sealed class PlatformVersionWindow : Window
{
    public DataGrid Versions { get; } = new() { Name = "PlatformVersions", AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column };
    public TextBox VersionBox { get; } = new() { Name = "ChosenVersion" };
    public ComboBox ArchitectureBox { get; } = new() { Name = "ChosenArchitecture", ItemsSource = new[] { "Авто", "x86", "x64" }, MinWidth = 100 };
    public Button ApplyButton { get; } = new() { Name = "ApplyPlatformVersion", Content = "Применить", IsDefault = true };
    public string SelectedVersion => VersionBox.Text.Trim();
    public string SelectedArchitecture => ArchitectureBox.SelectedItem as string ?? "Авто";

    public PlatformVersionWindow(IEnumerable<PlatformInfo> platforms, string version, string architecture, string baseName)
    {
        Title = "Выбор версии платформы"; Width = 650; Height = 480; MinWidth = 470; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; DialogBehavior.FitToScreen(this);
        var rows = platforms.Where(p => Version.TryParse(p.Version, out var v) && v.Major == 8 && p.Architecture is "x86" or "x64")
            .DistinctBy(p => (p.Version, p.Architecture, p.Directory)).ToArray();
        bool descending = true; string filter = "";
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 7) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMuted");
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var heading = new TextBlock { Text = baseName, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        foreach (var pair in new[] { ("Все", ""), ("x32", "x86"), ("x64", "x64") })
        {
            var choice = new RadioButton { Name = pair.Item2.Length == 0 ? "AllArchitectures" : "Only" + pair.Item2, Content = pair.Item1, GroupName = "VersionArchitecture", Margin = new Thickness(3, 6, 16, 3), IsChecked = pair.Item2.Length == 0 };
            choice.Checked += (_, _) => { filter = pair.Item2; if (filter.Length > 0) ArchitectureBox.SelectedItem = filter; Refresh(); }; tools.Children.Add(choice);
        }
        var ascending = new Button { Name = "VersionsAscending", Content = "↑", ToolTip = "Версии по возрастанию", Width = 30 };
        var newest = new Button { Name = "VersionsDescending", Content = "↓", ToolTip = "Версии по убыванию", Width = 30 };
        ascending.Click += (_, _) => { descending = false; Refresh(); }; newest.Click += (_, _) => { descending = true; Refresh(); };
        tools.Children.Add(ascending); tools.Children.Add(newest);
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var choiceGrid = new Grid(); choiceGrid.ColumnDefinitions.Add(new()); choiceGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var versionPanel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; versionPanel.Children.Add(new TextBlock { Text = "Версия (можно указать вручную)", Margin = new Thickness(0, 0, 0, 4) }); versionPanel.Children.Add(VersionBox); choiceGrid.Children.Add(versionPanel);
        var archPanel = new StackPanel(); archPanel.Children.Add(new TextBlock { Text = "Разрядность", Margin = new Thickness(0, 0, 0, 4) }); archPanel.Children.Add(ArchitectureBox); Grid.SetColumn(archPanel, 1); choiceGrid.Children.Add(archPanel); footer.Children.Add(choiceGrid);
        System.Windows.Automation.AutomationProperties.SetName(VersionBox, "Версия платформы"); System.Windows.Automation.AutomationProperties.SetName(ArchitectureBox, "Разрядность платформы");
        footer.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отмена", IsCancel = true }; cancel.Click += (_, _) => Close(); buttons.Children.Add(ApplyButton); buttons.Children.Add(cancel); footer.Children.Add(buttons);
        Versions.Columns.Add(new DataGridTextColumn { Header = "Версия", Binding = new Binding(nameof(PlatformInfo.Version)), Width = new DataGridLength(120) });
        Versions.Columns.Add(new DataGridTextColumn { Header = "Разрядность", Binding = new Binding(nameof(PlatformInfo.Architecture)), Width = new DataGridLength(95) });
        Versions.Columns.Add(new DataGridTextColumn { Header = "Каталог", Binding = new Binding(nameof(PlatformInfo.Directory)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); root.Children.Add(Versions);
        VersionBox.Text = version; ArchitectureBox.SelectedItem = architecture is "x86" or "x64" ? architecture : "Авто";
        Versions.SelectionChanged += (_, _) => { if (Versions.SelectedItem is PlatformInfo item) { VersionBox.Text = item.Version; ArchitectureBox.SelectedItem = item.Architecture; } };
        VersionBox.TextChanged += (_, _) => Validate(); ArchitectureBox.SelectionChanged += (_, _) => Validate();
        ApplyButton.Click += (_, _) => { if (Validate()) DialogResult = true; };
        Versions.MouseDoubleClick += (_, e) =>
        {
            if (!DialogBehavior.OnItem(e.OriginalSource) || Versions.SelectedItem is not PlatformInfo item) return;
            VersionBox.Text = item.Version; ArchitectureBox.SelectedItem = item.Architecture;
            if (Validate()) { e.Handled = true; DialogResult = true; }
        };
        Loaded += (_, _) => { if (Versions.SelectedItem != null) { Versions.ScrollIntoView(Versions.SelectedItem); Versions.Focus(); } else VersionBox.Focus(); };
        Refresh();

        void Refresh()
        {
            string selectedVersion = SelectedVersion, selectedArchitecture = SelectedArchitecture;
            var visible = rows.Where(p => filter.Length == 0 || p.Architecture == filter);
            Versions.ItemsSource = (descending ? visible.OrderByDescending(p => Version.Parse(p.Version)) : visible.OrderBy(p => Version.Parse(p.Version))).ThenBy(p => p.Architecture).ThenBy(p => p.Directory).ToArray();
            Versions.SelectedItem = Versions.Items.Cast<PlatformInfo>().FirstOrDefault(p => p.Version == selectedVersion && (selectedArchitecture == "Авто" || p.Architecture == selectedArchitecture));
            // Selection is a display convenience; preserve the user's automatic architecture choice.
            VersionBox.Text = selectedVersion; ArchitectureBox.SelectedItem = selectedArchitecture;
            Validate();
        }
        bool Validate()
        {
            bool valid = Version.TryParse(SelectedVersion, out var v) && v.Major == 8 && v.Minor >= 0;
            ApplyButton.IsEnabled = valid;
            status.Text = valid ? "Показано установленных платформ: " + Versions.Items.Count + ". Закреплённый путь EXE будет снят."
                : "Укажите версию 8.x, например 8.3 или 8.3.27.1508.";
            return valid;
        }
    }
}
