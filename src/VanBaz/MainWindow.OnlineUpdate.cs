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
        CreateOnlineUpdateWindow(checkOnOpen).ShowDialog();
    }
    private Window CreateOnlineUpdateWindow(bool checkOnOpen = false, Func<string, CancellationToken, Task<AppRelease>>? readFeed = null,
        Func<AppRelease, string, CancellationToken, Task<PreparedAppUpdate>>? downloadPackage = null, int countdownSeconds = 10)
    {
        var w = new Window { Owner = this, Title = "Обновления ВанБаз", Width = 800, Height = 590, MinWidth = 590, MinHeight = 390, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        DialogBehavior.FitToScreen(w);
        var root = new DockPanel { Margin = new(12) }; w.Content = root;
        var buttons = new WrapPanel { Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var panel = new StackPanel(); root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = "Текущая версия: " + typeof(App).Assembly.GetName().Version?.ToString(3), Margin = new(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "HTTPS-адрес канала обновлений (JSON)" });
        var address = new TextBox { Name = "AppUpdateAddress", Text = catalog.Settings.AppUpdateUrl, Margin = new(0, 4, 0, 8) }; panel.Children.Add(address);
        var auto = new CheckBox { Content = "Проверять при запуске (без автоматической установки)", IsChecked = catalog.Settings.CheckAppUpdates, Margin = new(0, 0, 0, 8) }; panel.Children.Add(auto);
        var autoDownload = new CheckBox { Name = "AutoDownloadUpdate", Content = "Автозагрузка после проверки в этом окне (отсчёт " + countdownSeconds + " с)", IsChecked = catalog.Settings.AutoDownloadAppUpdates, Margin = new(0, 0, 0, 8) }; panel.Children.Add(autoDownload);
        var status = new TextBlock { Text = "Укажите адрес собственного или доверенного сервера. По умолчанию канал не задан.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 10) }; panel.Children.Add(status);
        var counter = new TextBlock { Name = "AppUpdateCountdown", TextWrapping = TextWrapping.Wrap }; panel.Children.Add(counter);
        var cancelAuto = new Button { Name = "CancelAutoDownload", Content = "Отменить автозагрузку", HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed }; panel.Children.Add(cancelAuto);
        var progress = new ProgressBar { Height = 5, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed }; panel.Children.Add(progress);
        var notes = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 150, Margin = new(0, 8, 0, 0) }; panel.Children.Add(notes);
        var check = new Button { Name = "CheckAppRelease", Content = "Проверить", IsDefault = true }; var install = new Button { Name = "InstallAppRelease", Content = "Загрузить и установить…", IsEnabled = false }; var download = new Button { Name = "DownloadAppRelease", Content = "Загрузить", IsEnabled = false }; var local = new Button { Content = "Локальный ZIP…" }; var save = new Button { Content = "Сохранить настройки" }; var postpone = new Button { Name = "PostponeAppUpdate", Content = "Напомнить через сутки" }; var close = new Button { Content = "Закрыть", IsCancel = true };
        foreach (var b in new[] { check, download, install, local, save, postpone, close }) buttons.Children.Add(b);
        AppRelease? release = null; PreparedAppUpdate? readyUpdate = null; CancellationTokenSource? cancellation = null; string checkedUrl = "";
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; int remaining = 0; bool closed = false;
        void StopCountdown() { timer.Stop(); counter.Text = ""; cancelAuto.Visibility = Visibility.Collapsed; }
        void StartCountdown()
        {
            StopCountdown();
            if (closed || !CanEdit || release == null || readyUpdate != null || cancellation != null || autoDownload.IsChecked != true) return;
            remaining = Math.Clamp(countdownSeconds, 1, 60); counter.Text = "Загрузка через " + remaining + " с. Установка требует отдельного подтверждения."; cancelAuto.Visibility = Visibility.Visible; timer.Start();
        }
        void Busy(bool value) { bool allowed = !value && CanEdit; check.IsEnabled = local.IsEnabled = save.IsEnabled = address.IsEnabled = auto.IsEnabled = autoDownload.IsEnabled = postpone.IsEnabled = allowed; install.IsEnabled = allowed && release != null; download.IsEnabled = allowed && release != null && readyUpdate == null; install.Content = readyUpdate == null ? "Загрузить и установить…" : "Установить…"; close.Content = value ? "Отменить" : "Закрыть"; progress.Visibility = Visible(value); }
        async Task Check()
        {
            StopCountdown();
            try
            {
                string url = address.Text.Trim(); ConfigurationService.Https(url);
                if (isolatedSession && readFeed == null) { status.Text = "Сетевые запросы отключены в проверочной сессии."; return; }
                if (!Commit(() => { catalog.Settings.AppUpdateUrl = url; catalog.Settings.CheckAppUpdates = auto.IsChecked == true; catalog.Settings.AutoDownloadAppUpdates = autoDownload.IsChecked == true; catalog.Settings.AppUpdateRemindAfterUtc = null; })) return;
                release = null; readyUpdate = null; cancellation = new(); Busy(true); operations.Add("@feed"); status.Text = "Проверка канала…"; progress.IsIndeterminate = true;
                var found = await (readFeed?.Invoke(url, cancellation.Token) ?? AppUpdateFeed.Check(url, catalog.Copy().Settings, cancellation.Token));
                checkedUrl = url; notes.Text = found.Notes;
                bool newer = new Version(found.Version) > typeof(App).Assembly.GetName().Version;
                release = newer ? found : null;
                status.Text = newer ? "Доступна версия " + found.Version + " · " + (found.Size / 1024.0 / 1024).ToString("0.0") + " МБ.\n" + found.PackageUrl : "Канал не содержит более новой версии (" + found.Version + ").";
            }
            catch (OperationCanceledException) { status.Text = "Проверка отменена или истекло время ожидания."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { operations.Remove("@feed"); cancellation?.Dispose(); cancellation = null; Busy(false); StartCountdown(); }
        }
        save.Click += (_, _) => { try { string url = address.Text.Trim(); if (url.Length > 0) ConfigurationService.Https(url); else if (auto.IsChecked == true || autoDownload.IsChecked == true) throw new ArgumentException("Укажите канал для автоматической проверки и загрузки."); if (Commit(() => { catalog.Settings.AppUpdateUrl = url; catalog.Settings.CheckAppUpdates = auto.IsChecked == true; catalog.Settings.AutoDownloadAppUpdates = autoDownload.IsChecked == true; })) status.Text = "Настройки канала сохранены."; } catch (Exception ex) { status.Text = ex.Message; } };
        check.Click += async (_, _) => await Check();
        address.TextChanged += (_, _) => { StopCountdown(); release = null; readyUpdate = null; install.IsEnabled = download.IsEnabled = false; };
        async Task Download(bool installAfter)
        {
            StopCountdown();
            if (!CanEdit || release == null || isolatedSession && downloadPackage == null) return;
            try
            {
                cancellation = new(); Busy(true); operations.Add("@feed"); progress.IsIndeterminate = false; progress.Value = 0; status.Text = "Загрузка и проверка пакета…";
                var prepared = readyUpdate ?? await (downloadPackage?.Invoke(release, checkedUrl, cancellation.Token) ?? AppUpdateFeed.Download(release, checkedUrl, catalog.Copy().Settings, Path.Combine(store.Folder, "Updates"), new Progress<double>(v => progress.Value = v), cancellation.Token));
                readyUpdate = prepared; ProgramVersions.Archive(prepared.Folder, store.Folder);
                operations.Remove("@feed");
                if (!installAfter || isolatedSession) { status.Text = "ВанБаз " + prepared.Version + " загружен и проверен. Установите его кнопкой «Установить» или позже из сохранённых версий."; return; }
                if (MessageBox.Show(w, "Установить ВанБаз " + prepared.Version + "?\nПриложение будет перезапущено. Данные сохранятся.", "Установка", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) { status.Text = "Пакет проверен. Установка отменена."; return; }
                store.Save(catalog); store.Snapshot(); await AppUpdateService.StartInstaller(prepared, store.Folder, InterfaceMode, modeLocked); cancellation.Dispose(); cancellation = null; w.Close(); exitRequested = true; Close();
            }
            catch (OperationCanceledException) { status.Text = "Загрузка отменена или истекло время ожидания."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { operations.Remove("@feed"); cancellation?.Dispose(); cancellation = null; Busy(false); }
        }
        install.Click += async (_, _) => await Download(true); download.Click += async (_, _) => await Download(false);
        timer.Tick += async (_, _) => { if (--remaining <= 0) { StopCountdown(); await Download(false); } else counter.Text = "Загрузка через " + remaining + " с. Установка требует отдельного подтверждения."; };
        cancelAuto.Click += (_, _) => { StopCountdown(); status.Text = "Автозагрузка отменена для этой проверки."; };
        autoDownload.Checked += (_, _) => StartCountdown(); autoDownload.Unchecked += (_, _) => StopCountdown();
        postpone.Click += (_, _) => { if (Commit(() => catalog.Settings.AppUpdateRemindAfterUtc = DateTime.UtcNow.AddDays(1))) { AppUpdateButton.Visibility = Visibility.Collapsed; w.Close(); } };
        local.Click += (_, _) => { w.Close(); UpdateApp_Click(this, new RoutedEventArgs()); };
        close.Click += (_, _) => { StopCountdown(); if (cancellation != null) cancellation.Cancel(); else w.Close(); };
        w.Closing += (_, e) => { StopCountdown(); if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); } };
        w.Closed += (_, _) => { closed = true; StopCountdown(); };
        if (checkOnOpen) w.Loaded += async (_, _) => await Check();
        return w;
    }
    private async Task CheckAppUpdateOnStartup()
    {
        if (isolatedSession || PortableDistribution.IncludesRuntime(AppContext.BaseDirectory) || !catalog.Settings.CheckAppUpdates || string.IsNullOrWhiteSpace(catalog.Settings.AppUpdateUrl) || catalog.Settings.AppUpdateRemindAfterUtc > DateTime.UtcNow) return;
        try
        {
            var release = await AppUpdateFeed.Check(catalog.Settings.AppUpdateUrl, catalog.Copy().Settings, CancellationToken.None);
            if (IsLoaded && new Version(release.Version) > typeof(App).Assembly.GetName().Version)
            { AppUpdateButton.Content = "Обновление " + release.Version; AppUpdateButton.Visibility = Visibility.Visible; AppUpdateButton.ToolTip = "Открыть проверку и установку обновления ВанБаз"; }
        }
        catch (Exception ex) { if (IsLoaded) { AppUpdateButton.Content = "Канал обновлений"; AppUpdateButton.Visibility = Visibility.Visible; AppUpdateButton.ToolTip = ex.Message; } }
    }
}
