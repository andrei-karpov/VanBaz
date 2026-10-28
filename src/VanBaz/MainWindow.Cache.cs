using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow
{
    private void Cache_Click(object sender, RoutedEventArgs e)
    { if (AllowTools()) CreateCacheWindow().ShowDialog(); }
    private Window CreateCacheWindow(IEnumerable<string>? profiles = null, Func<bool>? clientsRunning = null, Action<CacheFolder, bool, bool>? execute = null)
    {
        clientsRunning ??= CacheService.ClientsRunning;
        execute ??= (item, restore, selective) => { if (isolatedSession) return; if (restore || !selective) CacheService.Move(item, restore); else CacheCleanup.ClearSelective(item); };
        var w = new Window { Owner = this, Title = "Очистка кэша 1С", Width = 990, Height = 650, MinWidth = 740, MinHeight = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner }; DialogBehavior.FitToScreen(w);
        var root = new DockPanel { Margin = new Thickness(12) }; w.Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Выберите профили Windows и каталоги баз. Выборочная очистка сохраняет свойства профилей и неизвестные файлы; грубая переносит выбранные GUID-каталоги целиком. Оба режима используют карантин.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var profileRow = new DockPanel(); top.Children.Add(profileRow);
        var pick = new Button { Content = "Добавить профиль…" }; DockPanel.SetDock(pick, Dock.Right); profileRow.Children.Add(pick);
        var profileList = new ListBox { Name = "CacheProfiles", Height = 65, SelectionMode = SelectionMode.Extended }; profileRow.Children.Add(profileList);
        var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) }; top.Children.Add(options);
        var local = new CheckBox { Content = "Local", IsChecked = true }; var roaming = new CheckBox { Content = "Roaming", IsChecked = true };
        var version = new ComboBox { ItemsSource = new[] { "Все", "1cv8", "1cv81", "1cv82" }, SelectedIndex = 0, Width = 110, ToolTip = "Каталог версии платформы" };
        var scope = new ComboBox { ItemsSource = new[] { "Все базы", "Только базы текущего списка", "Нет в текущем списке" }, SelectedIndex = 0, Width = 210 };
        var quarantine = new CheckBox { Content = "Карантин (восстановление)" };
        foreach (var c in new UIElement[] { local, roaming, version, scope, quarantine }) options.Children.Add(c);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 5) }; footer.Children.Add(status);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var list = new ListBox { Name = "CacheFolders", SelectionMode = SelectionMode.Extended }; root.Children.Add(list);
        var selected = new Button { Content = "Выбранная база", IsEnabled = Selected != null };
        var preview = new Button { Content = "Предпросмотр" }; var selective = new Button { Content = "Выборочная очистка…" }; var all = new Button { Content = "Грубая очистка…" }; var restore = new Button { Content = "Восстановить…" }; var refresh = new Button { Content = "Обновить" }; var close = new Button { Content = "Закрыть", IsCancel = true };
        foreach (var b in new[] { selected, preview, selective, all, restore, refresh, close }) buttons.Children.Add(b);
        bool busy = false;
        void States()
        {
            bool selection = list.SelectedItems.Count > 0 && !busy;
            selective.IsEnabled = all.IsEnabled = CanEdit && selection && quarantine.IsChecked != true;
            restore.IsEnabled = CanEdit && selection && quarantine.IsChecked == true; preview.IsEnabled = selection;
            close.IsEnabled = refresh.IsEnabled = top.IsEnabled = list.IsEnabled = !busy;
            selected.IsEnabled = !busy && Selected != null;
        }
        void Refresh()
        {
            if (busy) return;
            var errors = new List<string>(); var roots = new List<string>();
            foreach (string profile in profileList.SelectedItems)
                try { roots.AddRange(CacheService.ProfileRoots(profile, local.IsChecked == true, roaming.IsChecked == true, version.SelectedItem as string ?? "Все")); }
                catch (Exception ex) { errors.Add(profile + ": " + ex.Message); }
            var found = CacheService.Find(roots, catalog.Bases, quarantine.IsChecked == true, errors);
            var ids = catalog.Bases.Select(b => Guid.TryParse(b.Id, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();
            list.ItemsSource = found.Where(item => scope.SelectedIndex == 0 || (scope.SelectedIndex == 1 ? ids.Contains(Guid.Parse(item.BaseId)) : !ids.Contains(Guid.Parse(item.BaseId)))).ToArray();
            status.Text = "Каталогов: " + list.Items.Count + ". Профилей выбрано: " + profileList.SelectedItems.Count + (errors.Count == 0 ? "" : "\nНедоступно: " + string.Join("; ", errors)); States();
        }
        IEnumerable<string> StandardProfiles()
        {
            string current = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); yield return current;
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            if (key == null) yield break;
            foreach (string name in key.GetSubKeyNames()) { using var profile = key.OpenSubKey(name); if (profile?.GetValue("ProfileImagePath") is string path && !path.Equals(current, StringComparison.OrdinalIgnoreCase)) yield return Environment.ExpandEnvironmentVariables(path); }
        }
        foreach (string profile in (profiles ?? StandardProfiles()).Distinct(StringComparer.OrdinalIgnoreCase)) profileList.Items.Add(profile);
        if (profileList.Items.Count > 0) profileList.SelectedIndex = 0;
        pick.Click += (_, _) => { var picker = new OpenFolderDialog { Title = "Папка профиля Windows с AppData" }; if (picker.ShowDialog(w) == true) { if (!profileList.Items.Contains(picker.FolderName)) profileList.Items.Add(picker.FolderName); profileList.SelectedItems.Add(picker.FolderName); Refresh(); } };
        profileList.SelectionChanged += (_, _) => Refresh(); version.SelectionChanged += (_, _) => Refresh(); scope.SelectionChanged += (_, _) => Refresh();
        foreach (var check in new[] { local, roaming, quarantine }) { check.Checked += (_, _) => Refresh(); check.Unchecked += (_, _) => Refresh(); }
        refresh.Click += (_, _) => Refresh(); close.Click += (_, _) => w.Close(); list.SelectionChanged += (_, _) => States();
        selected.Click += (_, _) => { list.SelectedItems.Clear(); if (Selected != null && Guid.TryParse(Selected.Id, out var selectedId)) foreach (CacheFolder item in list.Items) if (Guid.TryParse(item.BaseId, out var id) && id == selectedId) list.SelectedItems.Add(item); };
        string Preview(IEnumerable<CacheFolder> items, bool partial)
        {
            return string.Join("\n\n", items.Select(item =>
            {
                if (quarantine.IsChecked == true) return "Восстановить без замены существующих файлов:\n" + item.Path;
                if (!partial) return "Каталог целиком:\n" + item.Path;
                var plan = CacheCleanup.Plan(item);
                return item.Path + "\nВ карантин:\n" + string.Join("\n", plan.Paths) + "\nСохранятся неизвестные файлы и свойства:\n" + string.Join("\n", plan.Preserved);
            }));
        }
        preview.Click += (_, _) => { try { FormDialog.ShowText(w, "Предпросмотр выборочной очистки", Preview(list.SelectedItems.Cast<CacheFolder>(), true)); } catch (Exception ex) { status.Text = ex.Message; } };
        async System.Threading.Tasks.Task Apply(bool partial, bool restoring)
        {
            if (busy || !CanEdit || list.SelectedItems.Count == 0) return;
            if (clientsRunning() || operations.Count > 0) { status.Text = "Завершите сеансы 1С и операции ВанБаз."; return; }
            var items = list.SelectedItems.Cast<CacheFolder>().ToArray();
            try
            {
                string text = Preview(items, partial);
                if (MessageBox.Show(w, text + "\n\nВыполнить выбранную операцию?", restoring ? "Восстановление кэша" : partial ? "Выборочная очистка" : "Грубая очистка", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                busy = true; States(); operations.Add("@cache"); var messages = new List<string>();
                await System.Threading.Tasks.Task.Run(() => { foreach (var item in items) try { if (clientsRunning()) throw new IOException("Во время обработки появился процесс 1С."); execute(item, restoring, partial); messages.Add("Готово: " + item.Path); } catch (Exception ex) { messages.Add("Пропущено: " + item.Path + "\n" + ex.Message); } });
                busy = false; Refresh(); FormDialog.ShowText(w, "Результат очистки", string.Join("\n\n", messages));
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { operations.Remove("@cache"); busy = false; States(); }
        }
        selective.Click += async (_, _) => await Apply(true, false); all.Click += async (_, _) => await Apply(false, false); restore.Click += async (_, _) => await Apply(false, true);
        w.Closing += (_, e) => { if (busy) { e.Cancel = true; status.Text = "Дождитесь окончания обработки."; } };
        Refresh(); return w;
    }
}
