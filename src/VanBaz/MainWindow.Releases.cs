using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow
{
    private void Configurations_Click(object sender, RoutedEventArgs e) => OpenConfigurations(false);
    private void ActualReleases_Click(object sender, RoutedEventArgs e) => OpenConfigurations(true);
    private void OpenConfigurations(bool watched)
    {
        if (!AllowTools()) return;
        var w = new Window { Owner = this, Title = watched ? "Актуальные релизы" : "Список конфигураций", Width = 1000, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(12) }; w.Content = root;
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var status = new TextBlock { Text = "Проверка выполняется по команде. Источники можно менять в свойствах конфигурации.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.Column, FontSize = catalog.Settings.FontSize, RowHeight = Math.Max(catalog.Settings.RowHeight, catalog.Settings.FontSize + 6) };
        void Column(string header, string property, double width) => grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = width });
        Column("Конфигурация", "Name", 280); Column("Релиз", "LatestVersion", 120); Column("Дата релиза", "ReleaseDate", 115); Column("Проверка", "Status", 330); root.Children.Add(grid);
        void Refresh() { var id = (grid.SelectedItem as ConfigurationSource)?.Id; grid.ItemsSource = catalog.Configurations.Where(c => !watched || c.Watch).ToList(); grid.SelectedItem = catalog.Configurations.FirstOrDefault(c => c.Id == id); }
        bool busy = false;
        var selectedButtons = new List<Button>();
        void Button(string text, Action action, bool selection = false) { var b = new Button { Content = text }; b.Click += (_, _) => { if (busy) return; try { action(); } catch (Exception ex) { Error(text, ex); } }; buttons.Children.Add(b); if (selection) selectedButtons.Add(b); }
        Button("Добавить…", () => { EditConfiguration(null, w); Refresh(); });
        Button("Свойства…", () => { if (grid.SelectedItem is ConfigurationSource c) { EditConfiguration(c, w); Refresh(); } }, true);
        Button("В отслеживание / убрать", () => { var items = grid.SelectedItems.Cast<ConfigurationSource>().ToArray(); Commit(() => { foreach (var item in items) item.Watch = !item.Watch; }); Refresh(); }, true);
        if (!watched) Button("Удалить", () =>
        {
            var items = grid.SelectedItems.Cast<ConfigurationSource>().ToArray();
            if (items.Length == 0 || MessageBox.Show(w, "Удалить выбранные источники: " + items.Length + "? Базы сохранятся.", "Конфигурации", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Commit(() => { foreach (var c in items) { catalog.Configurations.Remove(c); foreach (var b in catalog.Bases.Where(b => b.ConfigurationId == c.Id)) b.ConfigurationId = ""; } }); Refresh();
        }, true);
        var check = new Button { Content = "Проверить обновления" }; buttons.Children.Add(check);
        var cancel = new Button { Content = "Остановить", IsEnabled = false }; buttons.Children.Add(cancel); bool stop = false;
        cancel.Click += (_, _) => stop = true;
        check.Click += async (_, _) =>
        {
            if (busy) return;
            var items = grid.SelectedItems.Count > 0 ? grid.SelectedItems.Cast<ConfigurationSource>().ToArray() : catalog.Configurations.Where(c => c.Watch).ToArray();
            busy = true; stop = false; States();
            try
            {
                foreach (var c in items)
                {
                    if (stop) break; status.Text = "Проверяется: " + c.Name;
                    try
                    {
                        var info = await ReleaseService.Fetch(c.InfoUrl, catalog.Settings);
                        if (!Commit(() => { c.LatestVersion = info.Version; c.ReleaseDate = info.Date; c.Checked = DateTime.Now; c.Error = ""; })) break;
                    }
                    catch (Exception ex) { Commit(() => { c.Error = ex.Message; c.Checked = DateTime.Now; }); }
                    Refresh();
                }
                status.Text = stop ? "Проверка остановлена." : "Проверка завершена. Ошибки показаны напротив источников.";
            }
            finally { busy = false; States(); }
        };
        Button("Дистрибутивы…", () => { if (grid.SelectedItem is ConfigurationSource c) OpenDistributions(c, w); }, true);
        Button("Закрыть", w.Close);
        grid.MouseDoubleClick += (_, e) => { if (DialogBehavior.OnItem(e.OriginalSource) && !busy && grid.SelectedItem is ConfigurationSource c) { EditConfiguration(c, w); Refresh(); } };
        w.Closing += (_, args) => { if (busy) { stop = true; args.Cancel = true; status.Text = "Остановка после текущего запроса (до 20 секунд)…"; } };
        void States()
        {
            foreach (var button in buttons.Children.OfType<Button>()) button.IsEnabled = !busy;
            foreach (var button in selectedButtons) button.IsEnabled = !busy && grid.SelectedItem != null;
            cancel.IsEnabled = busy; check.IsEnabled = !busy && (grid.SelectedItems.Count > 0 || catalog.Configurations.Any(c => c.Watch));
        }
        grid.SelectionChanged += (_, _) => States();
        w.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; w.Close(); } };
        Refresh(); States(); w.ShowDialog();
    }
    private void EditConfiguration(ConfigurationSource? original, Window owner)
    {
        var c = original?.Copy() ?? new ConfigurationSource { Name = "Новая конфигурация" };
        var f = new FormDialog("Свойства конфигурации", 790, 620) { Owner = owner };
        f.Section("Описание"); f.Text("name", "Наименование", c.Name); f.Text("country", "Страна", c.Country);
        f.Text("short", "Короткое имя", c.ShortName); f.Text("edition", "Редакция", c.Edition); f.Text("platform", "Платформа", c.Platform);
        var url = f.Text("url", "UpdInfo.txt", c.InfoUrl);
        f.ActionButton("Составить адрес по полям", () => { url.Text = ConfigurationService.GeneratedUrl(new() { ShortName = f.Value("short"), Edition = f.Value("edition"), Platform = f.Value("platform") }); });
        f.Note("Адрес строится по схеме каталога поставщика. Проверьте его для своей редакции; успешная генерация адреса не означает, что он существует.");
        f.Check("watch", "Показывать в «Актуальных релизах»", c.Watch);
        f.Section("Дистрибутивы"); f.Text("download", "Корень загрузок HTTPS", c.DownloadRoot);
        f.Note("Адрес каталога обновлений из документации или метаданных конфигурации. Список файлов загружается из v8upd11.zip рядом с UpdInfo.txt. Доступ к закрытому серверу вводится только на время загрузки.");
        f.Validate = () => { if (f.Value("name").Length == 0) return f.SetError("Укажите название."); ConfigurationService.Https(f.Value("url")); if (f.Value("download").Length > 0) ConfigurationService.Https(f.Value("download")); return true; };
        if (f.ShowDialog() != true) return;
        c.Name = f.Value("name"); c.Country = f.Value("country"); c.ShortName = f.Value("short"); c.Edition = f.Value("edition"); c.Platform = f.Value("platform");
        c.InfoUrl = f.Value("url"); c.DownloadRoot = f.Value("download"); c.Watch = f.Flag("watch");
        Commit(() => { if (original == null) catalog.Configurations.Add(c); else catalog.Configurations[catalog.Configurations.IndexOf(original)] = c; });
    }
    private async void OpenDistributions(ConfigurationSource source, Window owner)
    {
        var w = new Window { Owner = owner, Title = "Дистрибутивы · " + source.Name, Width = 880, Height = 510, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(12) }; w.Content = root;
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var status = new TextBlock { Text = "Чтение списка…", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var list = new ListBox(); root.Children.Add(list); var download = new Button { Content = "Скачать…", IsEnabled = false }; buttons.Children.Add(download);
        var cancel = new Button { Content = "Отменить загрузку", IsEnabled = false }; buttons.Children.Add(cancel);
        var close = new Button { Content = "Закрыть" }; buttons.Children.Add(close); close.Click += (_, _) => w.Close();
        CancellationTokenSource? cancellation = null;
        cancel.Click += (_, _) => cancellation?.Cancel();
        w.Closing += (_, args) => { if (cancellation != null) { cancellation.Cancel(); args.Cancel = true; status.Text = "Отмена загрузки…"; } };
        download.Click += async (_, _) =>
        {
            if (list.SelectedItem is not Distribution d || cancellation != null) return;
            try
            {
                var f = new FormDialog("Загрузка дистрибутива", 780, 490) { Owner = w }; f.Section("Источник");
                f.Text("root", "Корень загрузок", source.DownloadRoot); f.Text("user", "Логин поставщика", ""); f.Password("password", "Пароль на этот раз", "", false);
                f.Note("Данные входа не сохраняются. Используется HTTPS и стандартная Basic-аутентификация; для веб-входа через личный кабинет скачайте файл в браузере.");
                f.Validate = () => { ConfigurationService.DistributionUri(f.Value("root"), d.File); return true; }; f.SetAcceptText("Выбрать файл");
                if (f.ShowDialog() != true) return;
                var uri = ConfigurationService.DistributionUri(f.Value("root"), d.File);
                var save = new SaveFileDialog { Title = "Сохранить дистрибутив", FileName = Path.GetFileName(d.File), Filter = "Дистрибутив|*" + Path.GetExtension(d.File), OverwritePrompt = false };
                if (save.ShowDialog(w) != true) return;
                if (File.Exists(save.FileName)) throw new IOException("Выберите новое имя: существующий файл не заменяется.");
                var credentials = f.Value("user").Length == 0 ? null : new NetworkCredential(f.Value("user"), SecretStore.Reveal(f.Secret("password", "")));
                cancellation = new(); download.IsEnabled = false; cancel.IsEnabled = true; status.Text = "Скачивается: " + d.Version;
                await ConfigurationService.Download(uri, save.FileName, d.Size, catalog.Settings, credentials, cancellation.Token);
                status.Text = "Сохранено: " + save.FileName + ". Установка запускается отдельно для выбранной базы.";
            }
            catch (OperationCanceledException) { status.Text = "Загрузка отменена; неполный файл удалён."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { cancellation?.Dispose(); cancellation = null; download.IsEnabled = true; cancel.IsEnabled = false; }
        };
        w.Show();
        try { list.ItemsSource = await ConfigurationService.Distributions(source, catalog.Settings); download.IsEnabled = true; status.Text = "Выберите релиз. Учитывайте допустимые исходные версии; цепочка обновлений автоматически не устанавливается."; }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private async void DetectConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || Selected is not { } b) return;
        var f = new FormDialog("Определить конфигурацию · " + b.Name, 720, 420) { Owner = this }; f.Section("Внешнее соединение");
        f.Choice("arch", "COMConnector", b.Architecture == "x86" ? "x86" : "x64", new[] { "x86", "x64" });
        f.Note("Будет открыто внешнее соединение с базой под пользователем Конфигуратора из её свойств. Нужен зарегистрированный COMConnector выбранной разрядности. Код внешнего соединения конфигурации может выполняться при подключении.");
        f.SetAcceptText("Определить"); if (f.ShowDialog() != true || isolatedSession) return;
        if (!operations.Add(b.Id)) { MessageBox.Show("Операция с базой уже выполняется."); return; }
        try
        {
            StatusText.Text = "Чтение метаданных: " + b.Name;
            var result = await PlatformBridge.Execute(b, "metadata", f.Value("arch"));
            if (!Version.TryParse(result.Version, out _)) throw new IOException("Платформа не вернула корректный номер версии.");
            Commit(() => { b.ConfigurationName = result.Name; b.ConfigurationVersion = result.Version; });
            StatusText.Text = result.Name + " · " + result.Version;
        }
        catch (Exception ex) { Error("Определение конфигурации", ex); }
        finally { operations.Remove(b.Id); }
    }
    private void SessionLock_Click(object sender, RoutedEventArgs e)
    {
        if (!Enabled("lock") || Selected is not { } b) return;
        var dialog = new FormDialog("Блокировка входа · " + b.Name, 690, 540) { Owner = this };
        dialog.Section("Блокировка"); dialog.Element(new SessionLockPanel(b.Copy(), !isolatedSession));
        dialog.SetAcceptText("Закрыть"); dialog.ShowDialog();
    }
}