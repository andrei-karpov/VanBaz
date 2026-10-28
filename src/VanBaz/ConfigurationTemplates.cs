using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace VanBaz;

public record ConfigurationTemplate(string Name, string Version, string Platform, string Purpose, string Source, string Vendor, string Status)
{
    public bool Available => Status.StartsWith("Доступен", StringComparison.Ordinal);
    public override string ToString() => Name + " · " + Version;
}
public static class ConfigurationTemplates
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1C", "1cv8", "tmplts");
    public static List<ConfigurationTemplate> Scan(IEnumerable<string> roots, string platform, CancellationToken token)
    {
        var result = new Dictionary<string, ConfigurationTemplate>(StringComparer.OrdinalIgnoreCase); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var pending = new Stack<string>(roots.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath));
        while (pending.Count > 0 && seen.Count < 10000)
        {
            token.ThrowIfCancellationRequested(); string folder = pending.Pop(); if (!seen.Add(folder)) continue;
            try
            {
                CacheService.EnsureOrdinaryPath(folder);
                foreach (string mft in Directory.EnumerateFiles(folder, "*.mft"))
                    foreach (var entry in ReadManifest(mft, platform)) result[entry.Source] = entry;
                foreach (string file in Directory.EnumerateFiles(folder, "*.cf")) result.TryAdd(file, new(Path.GetFileNameWithoutExtension(file), "", "", "Конфигурация", file, "Без манифеста", "Доступен"));
                foreach (string child in Directory.EnumerateDirectories(folder)) if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { result[folder] = new(Path.GetFileName(folder), "", "", "Ошибка чтения", folder, "Недоступные", ex.Message); }
        }
        if (pending.Count > 0) result["limit"] = new("Уточните каталоги: достигнут предел10000папок", "", "", "", "", "Недоступные", "Поиск ограничен");
        return result.Values.OrderBy(t => t.Vendor).ThenBy(t => t.Name).ThenByDescending(t => System.Version.TryParse(t.Version, out var v) ? v : new()).ToList();
    }
    public static List<ConfigurationTemplate> ReadManifest(string manifest, string platform)
    {
        if (new FileInfo(manifest).Length > 1024 * 1024) throw new InvalidDataException("Манифест шаблона слишком велик.");
        byte[] bytes = File.ReadAllBytes(manifest); string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); } catch (DecoderFallbackException) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); text = Encoding.GetEncoding(1251).GetString(bytes); }
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase) { [""] = new(StringComparer.OrdinalIgnoreCase) }; var current = sections[""];
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { current = new(StringComparer.OrdinalIgnoreCase); sections[line[1..^1]] = current; }
            else { int equal = line.IndexOf('='); if (equal > 0) current[line[..equal].Trim()] = line[(equal + 1)..].Trim(); }
        }
        var meta = sections[""]; var result = new List<ConfigurationTemplate>(); string root = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        foreach (var fields in sections.Where(s => s.Key.Length > 0).Select(s => s.Value).Where(s => s.ContainsKey("Source")))
        {
            string source = Path.GetFullPath(Path.Combine(root, fields["Source"])); string relative = Path.GetRelativePath(root, source); if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new InvalidDataException("Шаблон ссылается за пределы каталога манифеста.");
            string status = Path.GetExtension(source).Equals(".cf", StringComparison.OrdinalIgnoreCase) ? File.Exists(source) ? "Доступен" : "Файл отсутствует" : "Создание из этого формата не поддерживается (допускается CF)";
            string required = meta.GetValueOrDefault("AppVersion", "");
            if (status == "Доступен" && System.Version.TryParse(required, out var min) && System.Version.TryParse(platform, out var selected))
            {
                if (selected.Build < 0 && min.Build >= 0 && selected.Major == min.Major && selected.Minor == min.Minor) status = "Доступен; требуется сборка не ниже " + required;
                else if (selected < min) status = "Требуется платформа не ниже " + required;
            }
            result.Add(new(meta.GetValueOrDefault("Name", Path.GetFileName(root)), meta.GetValueOrDefault("Version", ""), required, fields.GetValueOrDefault("Catalog", fields.GetValueOrDefault("Destination", "")), source, meta.GetValueOrDefault("Vendor", "Без поставщика"), status));
        }
        return result;
    }
}

public sealed class ConfigurationTemplateWindow : Window
{
    public ConfigurationTemplate? SelectedTemplate { get; private set; }
    public ConfigurationTemplateWindow(IEnumerable<string> roots, string platform)
    {
        Title = "Установленные шаблоны конфигураций"; Width = 970; Height = 590; WindowStartupLocation = WindowStartupLocation.CenterOwner; DialogBehavior.FitToScreen(this);
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var top = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var scan = new Button { Content = "Обновить" }; DockPanel.SetDock(scan, Dock.Right); top.Children.Add(scan);
        var browse = new Button { Content = "Добавить каталог…" }; DockPanel.SetDock(browse, Dock.Right); top.Children.Add(browse);
        var paths = new TextBox { Text = string.Join(Environment.NewLine, roots), AcceptsReturn = true, Height = 58, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ToolTip = "Каталоги шаблонов, по одному в строке" }; top.Children.Add(paths);
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var choose = new Button { Content = "Выбрать", IsDefault = true, IsEnabled = false }; DockPanel.SetDock(choose, Dock.Right); footer.Children.Add(choose);
        var close = new Button { Content = "Отмена", IsCancel = true }; close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; footer.Children.Add(status);
        var tree = new TreeView { Width = 230, Margin = new Thickness(0, 8, 8, 0) }; DockPanel.SetDock(tree, Dock.Left); root.Children.Add(tree);
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Margin = new Thickness(0, 8, 0, 0) }; root.Children.Add(grid);
        foreach (var c in new[] { ("Наименование", "Name", 160d), ("Версия", "Version", 100d), ("Платформа", "Platform", 70d), ("Назначение", "Purpose", 190d), ("Источник", "Source", 200d), ("Состояние", "Status", 200d) }) grid.Columns.Add(new DataGridTextColumn { Header = c.Item1, Binding = new Binding(c.Item2), Width = c.Item3 });
        List<ConfigurationTemplate> rows = new(); CancellationTokenSource? cancellation = null;
        tree.SelectedItemChanged += (_, _) => grid.ItemsSource = tree.SelectedItem is TreeViewItem { Tag: string vendor } ? rows.Where(r => r.Vendor == vendor).ToList() : rows;
        grid.SelectionChanged += (_, _) => { choose.IsEnabled = cancellation == null && grid.SelectedItem is ConfigurationTemplate { Available: true }; status.Text = grid.SelectedItem is ConfigurationTemplate t ? t.Source + "\n" + t.Status : "Выберите доступный шаблон CF."; };
        void Select() { if (cancellation == null && grid.SelectedItem is ConfigurationTemplate { Available: true } item) { SelectedTemplate = item; DialogResult = true; } }
        choose.Click += (_, _) => Select(); grid.MouseDoubleClick += (_, e) => { if (DialogBehavior.OnItem(e.OriginalSource)) Select(); };
        async Task Refresh()
        {
            if (cancellation != null) return; cancellation = new(); scan.IsEnabled = browse.IsEnabled = choose.IsEnabled = grid.IsEnabled = tree.IsEnabled = false; status.Text = "Чтение каталогов шаблонов…";
            try { string[] selectedRoots = paths.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); rows = await Task.Run(() => ConfigurationTemplates.Scan(selectedRoots, platform, cancellation.Token)); tree.Items.Clear(); tree.Items.Add(new TreeViewItem { Header = "Все шаблоны", IsSelected = true }); foreach (string vendor in rows.Select(r => r.Vendor).Distinct()) tree.Items.Add(new TreeViewItem { Header = vendor, Tag = vendor }); grid.ItemsSource = rows; status.Text = "Найдено: " + rows.Count + ". Доступно CF: " + rows.Count(r => r.Available); }
            catch (OperationCanceledException) { } catch (Exception ex) { status.Text = ex.Message; }
            finally { cancellation?.Dispose(); cancellation = null; scan.IsEnabled = browse.IsEnabled = grid.IsEnabled = tree.IsEnabled = true; choose.IsEnabled = grid.SelectedItem is ConfigurationTemplate { Available: true }; }
        }
        scan.Click += async (_, _) => await Refresh(); browse.Click += async (_, _) => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) { paths.Text += Environment.NewLine + picker.FolderName; await Refresh(); } };
        Loaded += async (_, _) => await Refresh(); Closing += (_, _) => cancellation?.Cancel();
    }
}
