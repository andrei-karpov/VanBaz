using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow
{
    private void Cache_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || isolatedSession) return;
        var w = new Window { Owner = this, Title = "Кэш 1С и карантин", Width = 950, Height = 550, MinWidth = 740, MinHeight = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(12) }; w.Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Выберите каталоги (Ctrl — несколько). Очистка перемещает их в карантин. Закройте сеансы 1С.\nAPPDATA содержит пользовательские настройки. Несопоставленные GUID могут относиться к базам из другого списка.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
        var profileRow = new DockPanel(); top.Children.Add(profileRow);
        var pick = new Button { Content = "Другой профиль…", ToolTip = "Выбрать папку профиля Windows, например C:\\Users\\User. Доступ проверяется с текущими правами." }; DockPanel.SetDock(pick, Dock.Right); profileRow.Children.Add(pick);
        var current = new Button { Content = "Текущий пользователь" }; DockPanel.SetDock(current, Dock.Right); profileRow.Children.Add(current);
        var profile = new TextBlock { Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; profileRow.Children.Add(profile);
        var quarantine = new CheckBox { Content = "Показать карантин для восстановления", Margin = new(0, 8, 0, 8) }; top.Children.Add(quarantine);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 7, 0, 5) }; footer.Children.Add(status);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var list = new ListBox { SelectionMode = SelectionMode.Extended }; root.Children.Add(list);
        IEnumerable<string> roots = CacheService.Roots().ToArray();
        var selected = new Button { Content = "Кэш выбранной базы", IsEnabled = Selected != null }; var action = new Button { Content = "В карантин…" }; var refresh = new Button { Content = "Обновить" }; var close = new Button { Content = "Закрыть", IsCancel = true };
        foreach (var b in new[] { selected, action, refresh, close }) buttons.Children.Add(b);
        void Refresh()
        {
            var errors = new List<string>(); list.ItemsSource = CacheService.Find(roots, catalog.Bases, quarantine.IsChecked == true, errors);
            action.Content = quarantine.IsChecked == true ? "Восстановить…" : "В карантин…";
            status.Text = "Каталогов: " + list.Items.Count + (errors.Count == 0 ? "" : ". Недоступно: " + string.Join("; ", errors));
        }
        pick.Click += (_, _) =>
        {
            var d = new OpenFolderDialog { Title = "Папка профиля Windows с AppData" }; if (d.ShowDialog(w) != true) return;
            try { var paths = CacheService.ProfileRoots(d.FolderName); roots = paths; profile.Text = d.FolderName; Refresh(); } catch (Exception ex) { Error("Профиль Windows", ex); }
        };
        current.Click += (_, _) => { roots = CacheService.Roots().ToArray(); profile.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); Refresh(); };
        quarantine.Checked += (_, _) => Refresh(); quarantine.Unchecked += (_, _) => Refresh(); refresh.Click += (_, _) => Refresh(); close.Click += (_, _) => w.Close();
        selected.Click += (_, _) => { list.SelectedItems.Clear(); if (Selected != null) foreach (CacheFolder item in list.Items) if (Guid.TryParse(Selected.Id, out var id) && Guid.TryParse(item.BaseId, out var cacheId) && id == cacheId) list.SelectedItems.Add(item); };
        DialogBehavior.Selection(list, action);
        action.Click += (_, _) =>
        {
            var items = list.SelectedItems.Cast<CacheFolder>().ToList(); if (items.Count == 0) return;
            if (CacheService.ClientsRunning() || operations.Count > 0) { MessageBox.Show(w, "Сначала завершите сеансы 1С и операции ВанБаз."); return; }
            bool restore = quarantine.IsChecked == true;
            if (MessageBox.Show(w, (restore ? "Восстановить" : "Переместить в карантин") + " каталоги профиля «" + profile.Text + "»?\n\n" + string.Join("\n", items.Select(i => i.Path)), "Кэш 1С", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var messages = new List<string>();
            foreach (var item in items) try { CacheService.Move(item, restore); messages.Add("Готово: " + item.Path); } catch (Exception ex) { messages.Add("Пропущено: " + item.Path + "\n" + ex.Message); }
            Refresh(); FormDialog.ShowText(w, "Результат", string.Join("\n\n", messages));
        };
        Refresh(); w.ShowDialog();
    }
}
