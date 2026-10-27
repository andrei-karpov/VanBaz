using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public partial class MainWindow
{
    private void OnlineUpdate_Click(object sender, RoutedEventArgs e) => ShowOnlineUpdate();
    private void ShowOnlineUpdate(bool checkOnOpen = false)
    {
        if (!AllowTools()) return;
        if (ShowPortableUpdate()) return;
        var w = new Window { Owner = this, Title = "Обновления ВанБаз", Width = 700, Height = 480, MinWidth = 600, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(12) }; w.Content = root;
        var buttons = new WrapPanel { Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var panel = new StackPanel(); root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = "Текущая версия: " + typeof(App).Assembly.GetName().Version?.ToString(3), Margin = new(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "HTTPS-адрес канала обновлений (JSON)" });
        var address = new TextBox { Text = catalog.Settings.AppUpdateUrl, Margin = new(0, 4, 0, 8) }; panel.Children.Add(address);
        var auto = new CheckBox { Content = "Проверять при запуске (без автоматической установки)", IsChecked = catalog.Settings.CheckAppUpdates, Margin = new(0, 0, 0, 8) }; panel.Children.Add(auto);
        var status = new TextBlock { Text = "Укажите адрес собственного или доверенного сервера. По умолчанию канал не задан.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 10) }; panel.Children.Add(status);
        var progress = new ProgressBar { Height = 5, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed }; panel.Children.Add(progress);
        var notes = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 150, Margin = new(0, 8, 0, 0) }; panel.Children.Add(notes);
        var check = new Button { Content = "Проверить", IsDefault = true }; var install = new Button { Content = "Загрузить и установить…", IsEnabled = false }; var local = new Button { Content = "Локальный ZIP…" }; var save = new Button { Content = "Сохранить настройки" }; var close = new Button { Content = "Закрыть", IsCancel = true };
        foreach (var b in new[] { check, install, local, save, close }) buttons.Children.Add(b);
        AppRelease? release = null; CancellationTokenSource? cancellation = null; string checkedUrl = "";
        void Busy(bool value) { check.IsEnabled = local.IsEnabled = save.IsEnabled = address.IsEnabled = auto.IsEnabled = !value; install.IsEnabled = !value && release != null; close.Content = value ? "Отменить" : "Закрыть"; progress.Visibility = Visible(value); }
        async Task Check()
        {
            try
            {
                string url = address.Text.Trim(); ConfigurationService.Https(url);
                if (!Commit(() => { catalog.Settings.AppUpdateUrl = url; catalog.Settings.CheckAppUpdates = auto.IsChecked == true; })) return;
                release = null; cancellation = new(); Busy(true); operations.Add("@feed"); status.Text = "Проверка канала…"; progress.IsIndeterminate = true;
                var found = await AppUpdateFeed.Check(url, catalog.Copy().Settings, cancellation.Token);
                checkedUrl = url; notes.Text = found.Notes;
                bool newer = new Version(found.Version) > typeof(App).Assembly.GetName().Version;
                release = newer ? found : null;
                status.Text = newer ? "Доступна версия " + found.Version + " · " + (found.Size / 1024.0 / 1024).ToString("0.0") + " МБ.\n" + found.PackageUrl : "Канал не содержит более новой версии (" + found.Version + ").";
            }
            catch (OperationCanceledException) { status.Text = "Проверка отменена или истекло время ожидания."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { operations.Remove("@feed"); cancellation?.Dispose(); cancellation = null; Busy(false); }
        }
        save.Click += (_, _) => { try { string url = address.Text.Trim(); if (url.Length > 0) ConfigurationService.Https(url); else if (auto.IsChecked == true) throw new ArgumentException("Укажите канал для автоматической проверки."); if (Commit(() => { catalog.Settings.AppUpdateUrl = url; catalog.Settings.CheckAppUpdates = auto.IsChecked == true; })) status.Text = "Настройки канала сохранены."; } catch (Exception ex) { status.Text = ex.Message; } };
        check.Click += async (_, _) => await Check();
        address.TextChanged += (_, _) => { release = null; install.IsEnabled = false; };
        install.Click += async (_, _) =>
        {
            if (release == null || isolatedSession) return;
            try
            {
                cancellation = new(); Busy(true); operations.Add("@feed"); progress.IsIndeterminate = false; progress.Value = 0; status.Text = "Загрузка и проверка пакета…";
                var prepared = await AppUpdateFeed.Download(release, checkedUrl, catalog.Copy().Settings, Path.Combine(store.Folder, "Updates"), new Progress<double>(v => progress.Value = v), cancellation.Token);
                operations.Remove("@feed");
                if (MessageBox.Show(w, "Установить ВанБаз " + prepared.Version + "?\nПриложение будет перезапущено. Данные сохранятся.", "Установка", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) { status.Text = "Пакет проверен. Установка отменена."; return; }
                store.Save(catalog); store.Snapshot(); await AppUpdateService.StartInstaller(prepared, store.Folder); cancellation.Dispose(); cancellation = null; w.Close(); exitRequested = true; Close();
            }
            catch (OperationCanceledException) { status.Text = "Загрузка отменена или истекло время ожидания."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { operations.Remove("@feed"); cancellation?.Dispose(); cancellation = null; Busy(false); }
        };
        local.Click += (_, _) => { w.Close(); UpdateApp_Click(this, new RoutedEventArgs()); };
        close.Click += (_, _) => { if (cancellation != null) cancellation.Cancel(); else w.Close(); };
        w.Closing += (_, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); } };
        if (checkOnOpen) w.Loaded += async (_, _) => await Check();
        w.ShowDialog();
    }
    private async Task CheckAppUpdateOnStartup()
    {
        if (isolatedSession || PortableDistribution.IncludesRuntime(AppContext.BaseDirectory) || !catalog.Settings.CheckAppUpdates || string.IsNullOrWhiteSpace(catalog.Settings.AppUpdateUrl)) return;
        try
        {
            var release = await AppUpdateFeed.Check(catalog.Settings.AppUpdateUrl, catalog.Copy().Settings, CancellationToken.None);
            if (IsLoaded && new Version(release.Version) > typeof(App).Assembly.GetName().Version)
            { AppUpdateButton.Content = "Обновление " + release.Version; AppUpdateButton.Visibility = Visibility.Visible; AppUpdateButton.ToolTip = "Открыть проверку и установку обновления ВанБаз"; }
        }
        catch (Exception ex) { if (IsLoaded) { AppUpdateButton.Content = "Канал обновлений"; AppUpdateButton.Visibility = Visibility.Visible; AppUpdateButton.ToolTip = ex.Message; } }
    }
}
