using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VanBaz;

public partial class MainWindow
{
    private void RunProfile(LaunchProfile profile, bool customize = false)
    {
        if (Selected is not { } b || !Enabled(profile.Designer ? "designer" : "launch")) return;
        try
        {
            if (customize)
            {
                var dialog = new LaunchOptionsDialog(b, profile.CopyForRun(), platforms, examples: CreateParameterExamples()) { Owner = this };
                if (dialog.ShowDialog() != true) return;
                profile = dialog.Read();
                if (dialog.SaveProfile && !Commit(() => catalog.LaunchProfiles.Add(profile))) return;
            }
            Launch(profile.Designer, temporary: profile.Apply(b), clientOverride: profile.Client);
        }
        catch (Exception ex) { Error("Профиль запуска", ex); }
    }
    private void LaunchProfiles_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } b || !AllowTools()) return;
        var window = CreateLaunchProfilesWindow(b); window.Owner = this; window.ShowDialog();
    }
    private Window CreateLaunchProfilesWindow(BaseEntry b)
    {
        var w = new Window { Title = "Профили запуска · " + b.Name, Width = 800, Height = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(12) }; w.Content = root;
        var footer = new WrapPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var list = new ListBox(); root.Children.Add(list);
        void Refresh() { list.ItemsSource = catalog.LaunchProfiles.Where(p => p.AppliesTo(b)).ToList(); if (list.Items.Count > 0) list.SelectedIndex = 0; }
        Button Add(string text, Action action) { var button = new Button { Content = text }; button.Click += (_, _) => { try { action(); } catch (Exception ex) { Error("Профили запуска", ex); } }; footer.Children.Add(button); return button; }
        Add("Новый…", () => { var f = new LaunchOptionsDialog(b, LaunchProfile.From(b, false, CurrentClient), platforms, true, examples: CreateParameterExamples()) { Owner = w }; if (f.ShowDialog() == true && Commit(() => catalog.LaunchProfiles.Add(f.Read()))) Refresh(); });
        var edit = Add("Изменить…", () => { if (list.SelectedItem is not LaunchProfile p) return; var f = new LaunchOptionsDialog(b, p, platforms, true, examples: CreateParameterExamples()) { Owner = w }; if (f.ShowDialog() == true && Commit(() => catalog.LaunchProfiles[catalog.LaunchProfiles.FindIndex(x => x.Id == p.Id)] = f.Read())) Refresh(); });
        var remove = Add("Удалить…", () => { if (list.SelectedItem is LaunchProfile p && MessageBox.Show(w, "Удалить профиль «" + p.Name + "»?", "Профиль", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes && Commit(() => catalog.LaunchProfiles.Remove(p))) Refresh(); });
        var run = Add("Запустить", () => { if (list.SelectedItem is LaunchProfile p) { w.Close(); RunProfile(p); } });
        var once = Add("Изменить на один запуск…", () => { if (list.SelectedItem is LaunchProfile p) { w.Close(); RunProfile(p, customize: true); } });
        var export = Add("Экспорт…", () =>
        {
            if (list.SelectedItem is not LaunchProfile p) return;
            var save = new Microsoft.Win32.SaveFileDialog { Filter = "Профиль ВанБаз|*.splaunch.json", FileName = "launch-profile.splaunch.json", Title = "Экспорт без секретов и локальных путей" };
            if (save.ShowDialog(w) == true) { LaunchProfileExchange.Export(p, save.FileName); MessageBox.Show(w, LaunchProfileExchange.Notice, "Профиль экспортирован"); }
        });
        Add("Импорт…", () =>
        {
            var open = new Microsoft.Win32.OpenFileDialog { Filter = "Профиль ВанБаз|*.splaunch.json;*.json" }; if (open.ShowDialog(w) != true) return;
            var p = LaunchProfileExchange.Import(open.FileName); p.BaseId = b.Id;
            var review = new LaunchOptionsDialog(b, p, platforms, true, examples: CreateParameterExamples()) { Owner = w };
            if (review.ShowDialog() == true && Commit(() => catalog.LaunchProfiles.Add(review.Read()))) Refresh();
        });
        var close = Add("Закрыть", () => w.Close()); close.IsCancel = true; DialogBehavior.Selection(list, edit, remove, run, once, export); Refresh(); return w;
    }
    private async void WebClient_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { Kind: "Web" } b || !Enabled("launch") || isolatedSession) return;
        string op = "web-launch:" + b.Id; operations.Add(op);
        var entry = b.Copy(); string browser = catalog.Settings.WebBrowser;
        try
        {
            using var process = await Task.Run(() => LaunchService.Start(WebClientLauncher.Build(entry, browser)));
            Commit(() => { b.LastLaunch = DateTime.Now; catalog.History.Insert(0, new() { BaseId = b.Id, Name = b.Name, Time = DateTime.Now, Mode = "Веб-клиент" }); catalog.History = catalog.History.Take(200).ToList(); });
            StatusText.Text = "Открыт веб-клиент: " + b.Name;
        }
        catch (Exception ex) { Error("Веб-клиент", ex); }
        finally { operations.Remove(op); }
    }
    private void BaseKeys_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        var f = CreateBaseKeysDialog(); f.Owner = SettingsOwner;
        f.ShowDialog();
    }
    private FormDialog CreateBaseKeysDialog()
    {
        var f = new FormDialog("Клавиши отдельных баз", 850, 600); f.Section("Привязки");
        string Label(BaseEntry b) => b.Name + " [" + b.Id + "]";
        var choices = catalog.Bases.Where(b => catalog.Settings.EnableLegacy77 || b.Version != "7.7").OrderBy(b => b.Name).Select(Label).Prepend("Не назначено").ToList();
        var originalBindings = new Dictionary<string, string>(catalog.Settings.BaseKeys);
        var labels = catalog.Bases.ToDictionary(Label, b => b.Id);
        for (int i = 1; i <= 5; i++)
        {
            string slot = i.ToString(), id = originalBindings.GetValueOrDefault(slot, "");
            var b = catalog.Bases.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            string selected = b != null ? Label(b) : id.Length == 0 ? "Не назначено" : "Недоступная база [" + id + "]";
            if (id.Length > 0) labels[selected] = id;
            f.Choice(slot, "Ctrl+Alt+" + i, selected, choices); f.PreserveChoice(slot, selected);
        }
        f.Note("Привязки скрытых и недоступных баз сохраняются. Для удаления привязки выберите «Не назначено». Запуск 7.7 доступен только после включения её поддержки.");
        f.ApplyChanges = () => Commit(() =>
        {
            var bindings = new Dictionary<string, string>(catalog.Settings.BaseKeys);
            for (int i = 1; i <= 5; i++) { string slot = i.ToString(); if (labels.TryGetValue(f.Value(slot), out string? id)) bindings[slot] = id; else bindings.Remove(slot); }
            catalog.Settings.BaseKeys = bindings;
        });
        f.Note("Действуют только в окне ВанБаз. Запускают Предприятие по ID базы, независимо от текущего поиска и выделения. Глобальные сочетания самого менеджера должны отличаться.");
        f.Validate = () =>
        {
            if (catalog.Settings.HotkeysEnabled)
            {
                var global = new[] { catalog.Settings.ShowHotkey, catalog.Settings.TrayHotkey, catalog.Settings.ScreenshotHotkey }.Select(GlobalHotkeys.Parse).ToList();
                for (int i = 1; i <= 5; i++) if (f.Value(i.ToString()) != "Не назначено" && global.Contains(GlobalHotkeys.Parse("Ctrl+Alt+" + i))) return f.SetError("Сочетание Ctrl+Alt+" + i + " занято глобальным действием менеджера.");
            }
            return true;
        };
        return f;
    }
    private bool HandleBaseLaunchKey(Key key, ModifierKeys modifiers, bool repeat)
    {
        if (modifiers != (ModifierKeys.Control | ModifierKeys.Alt) || key < Key.D1 || key > Key.D5) return false;
        string slot = ((int)key - (int)Key.D0).ToString();
        if (!catalog.Settings.BaseKeys.ContainsKey(slot)) return false;
        if (!repeat) _ = RunBoundBaseAsync(slot);
        return true;
    }
    private async Task RunBoundBaseAsync(string slot)
    {
        if (locked || operations.Count > 0) return;
        if (!catalog.Settings.BaseKeys.TryGetValue(slot, out var id) || !catalog.Bases.Any(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        { StatusText.Text = "База привязки удалена. Назначьте клавишу заново."; return; }
        if (!SelectBaseInList(id)) return;
        await LaunchAsync(false);
    }
    private void BaseLaunchKeysMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, BaseLaunchKeysMenu)) return;
        BaseLaunchKeysMenu.Items.Clear();
        for (int i = 1; i <= 5; i++)
        {
            string slot = i.ToString();
            var b = catalog.Bases.FirstOrDefault(b => b.Id.Equals(catalog.Settings.BaseKeys.GetValueOrDefault(slot), StringComparison.OrdinalIgnoreCase));
            var item = new MenuItem { Header = b?.Name ?? (catalog.Settings.BaseKeys.ContainsKey(slot) ? "База удалена" : "Не назначено"), InputGestureText = "Ctrl+Alt+" + slot, IsEnabled = b != null && (catalog.Settings.EnableLegacy77 || b.Version != "7.7") && !locked && operations.Count == 0 };
            item.Click += async (_, _) => await RunBoundBaseAsync(slot); BaseLaunchKeysMenu.Items.Add(item);
        }

    }
    private async void PlatformRoots_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        var window = new PlatformSettingsWindow(catalog.Settings, platforms) { Owner = SettingsOwner };
        window.ApplyChanges = () => Commit(() =>
        {
            catalog.Settings.PlatformRoots = window.Roots.Select(r => r.Path).ToList();
            catalog.Settings.DisabledPlatformRoots = window.Roots.Where(r => !r.Enabled).Select(r => r.Path).ToList();
            catalog.Settings.WebBrowser = window.Browser;
        });
        if (window.ShowDialog() == true) await RefreshPlatformsAsync(window.Result);
    }
}
