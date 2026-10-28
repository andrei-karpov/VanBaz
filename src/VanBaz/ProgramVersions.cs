using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace VanBaz;

public record ProgramVersion(string Version, string Folder, DateTime Saved, string Status)
{ public bool Valid => Status == "Проверена"; }
public static class ProgramVersions
{
    public static AppPackage Manifest(string folder)
    {
        var assembly = AssemblyName.GetAssemblyName(Path.Combine(folder, "VanBaz.dll"));
        if (assembly.Name != "VanBaz" || assembly.Version == null) throw new InvalidDataException("Ожидалась сборка VanBaz.dll.");
        var result = new AppPackage { Version = assembly.Version.ToString(3) };
        foreach (string file in AppUpdateService.Files) { string path = Path.Combine(folder, file); CacheService.EnsureOrdinaryPath(path); using var input = File.OpenRead(path); result.Files[file] = Convert.ToHexString(SHA256.HashData(input)); }
        return result;
    }
    public static PreparedAppUpdate Validate(string folder)
    {
        CacheService.EnsureOrdinaryPath(folder);
        var expected = JsonSerializer.Deserialize<AppPackage>(File.ReadAllText(Path.Combine(folder, "vanbaz-package.json"))) ?? throw new InvalidDataException("Нет манифеста версии.");
        var actual = Manifest(folder);
        if (expected.Version != actual.Version || expected.Files == null || expected.Files.Count != actual.Files.Count || actual.Files.Any(p => !expected.Files.TryGetValue(p.Key, out var hash) || !p.Value.Equals(hash, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Файлы сохранённой версии изменились. Восстановление запрещено.");
        return new(Path.GetFullPath(folder), actual.Version);
    }
    public static PreparedAppUpdate Stage(PreparedAppUpdate source, string dataFolder)
    {
        Validate(source.Folder); string folder = Path.Combine(Path.GetFullPath(dataFolder), "Updates", "restore-" + Guid.NewGuid().ToString("N")); CacheService.EnsureOrdinaryPath(folder); Directory.CreateDirectory(folder);
        foreach (string file in AppUpdateService.Files.Append("vanbaz-package.json")) File.Copy(Path.Combine(source.Folder, file), Path.Combine(folder, file));
        return Validate(folder);
    }
    public static string Archive(string source, string dataFolder)
    {
        var manifest = Manifest(source); string root = Path.Combine(Path.GetFullPath(dataFolder), "ProgramVersions"); CacheService.EnsureOrdinaryPath(root); Directory.CreateDirectory(root);
        foreach (var version in List(dataFolder).Where(v => v.Valid && v.Version == manifest.Version))
        { var saved = JsonSerializer.Deserialize<AppPackage>(File.ReadAllText(Path.Combine(version.Folder, "vanbaz-package.json")))!; if (manifest.Files.All(p => saved.Files.GetValueOrDefault(p.Key) == p.Value)) return version.Folder; }
        string folder = Path.Combine(root, manifest.Version + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(folder);
        foreach (string file in AppUpdateService.Files) File.Copy(Path.Combine(source, file), Path.Combine(folder, file));
        File.WriteAllText(Path.Combine(folder, "vanbaz-package.json"), JsonSerializer.Serialize(manifest)); Validate(folder); return folder;
    }
    public static List<ProgramVersion> List(string dataFolder)
    {
        string root = Path.Combine(Path.GetFullPath(dataFolder), "ProgramVersions"); if (!Directory.Exists(root)) return new(); CacheService.EnsureOrdinaryPath(root);
        return Directory.EnumerateDirectories(root).OrderByDescending(p => Directory.GetCreationTime(p)).Select(folder =>
        { try { var valid = Validate(folder); return new ProgramVersion(valid.Version, folder, Directory.GetCreationTime(folder), "Проверена"); } catch (Exception ex) { return new ProgramVersion(Path.GetFileName(folder).Split('-')[0], folder, Directory.GetCreationTime(folder), ex.Message); } }).ToList();
    }
}

public partial class MainWindow
{
    private Window CreateVersionHistoryWindow()
    {
        var window = new Window { Owner = this, Title = "Восстановить версию ВанБаз", Width = 800, Height = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner }; DialogBehavior.FitToScreen(window);
        var root = new DockPanel { Margin = new Thickness(12) }; window.Content = root;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var grid = new DataGrid { Name = "ProgramVersionsGrid", AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
        foreach (var column in new[] { ("Версия", "Version", 85d), ("Сохранена", "Saved", 150d), ("Файл / каталог", "Folder", 310d), ("Проверка", "Status", 180d) }) grid.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2), Width = column.Item3 }); root.Children.Add(grid);
        bool busy = false;
        var restore = new Button { Content = "Восстановить", IsEnabled = false }; buttons.Children.Add(restore);
        var import = new Button { Content = "Добавить ZIP…" }; buttons.Children.Add(import);
        var save = new Button { Content = "Сохранить текущую версию" }; buttons.Children.Add(save);
        var close = new Button { Content = "Закрыть", IsCancel = true }; close.Click += (_, _) => window.Close(); buttons.Children.Add(close);
        void States() { restore.IsEnabled = !busy && CanEdit && grid.SelectedItem is ProgramVersion { Valid: true }; import.IsEnabled = save.IsEnabled = !busy && CanEdit; close.IsEnabled = !busy; }
        void Refresh() { grid.ItemsSource = ProgramVersions.List(store.Folder); status.Text = grid.Items.Count == 0 ? "Сохранённых версий пока нет. Добавьте компактный ZIP из Releases или сохраните текущую версию." : "Восстанавливаются только файлы программы. Базы и настройки остаются в текущем каталоге данных."; States(); }
        grid.SelectionChanged += (_, _) => States();
        save.Click += (_, _) => { try { ProgramVersions.Archive(AppContext.BaseDirectory, store.Folder); Refresh(); } catch (Exception ex) { status.Text = ex.Message; } };
        import.Click += async (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Добавить версию из доверенного ZIP", Filter = "ВанБаз ZIP|*.zip" }; if (picker.ShowDialog(window) != true) return;
            busy = true; States();
            try { var prepared = await Task.Run(() => AppUpdateService.Prepare(picker.FileName, Path.Combine(store.Folder, "Updates"))); ProgramVersions.Archive(prepared.Folder, store.Folder); Refresh(); }
            catch (Exception ex) { status.Text = ex.Message; } finally { busy = false; States(); }
        };
        restore.Click += async (_, _) =>
        {
            if (!CanEdit || grid.SelectedItem is not ProgramVersion version) return;
            if (MessageBox.Show(window, "Восстановить ВанБаз " + version.Version + " и перезапустить программу?\nФайлы данных не заменяются. Более старая версия может не понимать новые настройки. Перед заменой будет сохранён снимок каталога.\n" + version.Folder, "Восстановление версии", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes || isolatedSession) return;
            busy = true; States(); operations.Add("@app-rollback");
            try { var prepared = ProgramVersions.Validate(version.Folder); store.Save(catalog); store.Snapshot(); await AppUpdateService.StartInstaller(prepared, store.Folder, InterfaceMode, modeLocked); operations.Remove("@app-rollback"); busy = false; window.Close(); exitRequested = true; Close(); }
            catch (Exception ex) { status.Text = ex.Message; } finally { operations.Remove("@app-rollback"); busy = false; States(); }
        };
        window.Closing += (_, e) => { if (busy) { e.Cancel = true; status.Text = "Дождитесь окончания операции."; } }; Refresh(); return window;
    }
    private void VersionHistory_Click(object sender, RoutedEventArgs e) { if (AllowTools()) CreateVersionHistoryWindow().ShowDialog(); }
}
