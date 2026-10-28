using System.IO;
using System.Windows;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace VanBaz;

public partial class App : Application
{
    internal System.Diagnostics.ProcessStartInfo? RestartStartInfo { get; set; }
    internal bool IsFatalShutdown { get; private set; }
    private bool presentingFatalError;
    private StartupOptions? startupOptions;
    private string? startupData;
    private string startupMode = "Специалист";
    private Mutex? catalogMutex;
    private bool ownsMutex;
    private InstanceBridge? bridge;
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            FatalError("Необработанная ошибка ВанБаз", args.Exception);
        };
        base.OnStartup(e);
        try
        {
            var options = StartupOptions.Parse(e.Args, AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            var data = options.DataFolder ?? Path.Combine(AppContext.BaseDirectory, "Data");
            startupOptions = options; startupData = data;
            string mutexName = "Local\\VanBaz-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(data).TrimEnd('\\').ToUpperInvariant())));
            catalogMutex = new Mutex(false, mutexName);
            try { ownsMutex = catalogMutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex)
            {
                if (e.Args.Contains("--import-only")) throw new IOException("Закройте ВанБаз перед переносом через командную строку.");
                InstanceBridge.Send(mutexName.Replace("Local\\", ""), e.Args);
                Shutdown(); return;
            }
            var store = new CatalogStore(data);
            var catalog = store.Load();
            startupMode = options.Mode ?? catalog.Settings.InterfaceMode;
            int migrationIndex = Array.IndexOf(e.Args, "--import-startmanager");
            if (migrationIndex >= 0)
            {
                string settings = migrationIndex + 1 < e.Args.Length ? e.Args[migrationIndex + 1] : StartManagerMigration.DefaultSettings;
                int listIndex = Array.IndexOf(e.Args, "--list");
                string list = listIndex >= 0 && listIndex + 1 < e.Args.Length ? e.Args[listIndex + 1] : StartManagerMigration.DefaultList;
                var migrated = StartManagerMigration.Prepare(catalog, settings, list);
                FeatureMigration.Apply(migrated);
                if (File.Exists(store.FilePath)) store.Snapshot();
                store.Save(migrated); catalog = migrated;
                File.WriteAllText(Path.Combine(data, "migration-report.txt"), $"Перенос StartManager → ВанБаз\n{catalog.Migration!.Time:dd.MM.yyyy HH:mm}\nНастройки: {catalog.Migration.SettingsFolder}\nСписок: {catalog.Migration.ListFile}\n\n{catalog.Migration.Summary}");
            }
            if (!store.HasPendingRecovery() && catalog.FeatureMigrationVersion < FeatureMigration.CurrentVersion)
            {
                if (File.Exists(store.FilePath)) store.Snapshot();
                FeatureMigration.Apply(catalog); store.Save(catalog);
            }
            else SnapshotService.SaveAutomatic(store, catalog, DateTime.Now);
            int themeIndex = Array.IndexOf(e.Args, "--import-themes");
            if (themeIndex >= 0)
            {
                if (themeIndex + 1 >= e.Args.Length) throw new ArgumentException("Укажите папку после --import-themes.");
                var prepared = catalog.Copy();
                ThemeService.ImportFolder(prepared.Settings, e.Args[themeIndex + 1]);
                string sourceName = prepared.Settings.SourceSettings.GetValueOrDefault("StyleName", "");
                if (prepared.Settings.Themes.Any(t => t.Name == sourceName)) prepared.Settings.Theme = sourceName;
                prepared.Settings.FavoriteThemes = prepared.Settings.Themes.Select(t => t.Name).OrderByDescending(n => n == prepared.Settings.Theme).Take(5).ToList();
                if (File.Exists(store.FilePath)) store.Snapshot();
                store.Save(prepared); catalog = prepared;
            }
            if (catalog.Migration is { } migration)
                File.WriteAllText(Path.Combine(data, "migration-report.txt"), $"Перенос StartManager → ВанБаз {typeof(App).Assembly.GetName().Version?.ToString(3)}\n{migration.Time:dd.MM.yyyy HH:mm}\n\n{migration.Summary}\n\nТекущий каталог: {catalog.Bases.Count} баз, {catalog.Groups.Count} групп, {catalog.Commands.Count} команд. Тема: {catalog.Settings.Theme}; импортированных палитр: {catalog.Settings.Themes.Count}.");
            if (e.Args.Contains("--import-only")) { Shutdown(0); return; }
            ThemeService.Apply(catalog.Settings);
            var window = new MainWindow(store, catalog, options, useOfficialLists: true); MainWindow = window;
            bridge = new InstanceBridge(mutexName.Replace("Local\\", ""), args => Dispatcher.InvokeAsync(() => window.HandleArguments(args)));
            MainWindow.Show();
            window.HandleArguments(e.Args);
        }
        catch (Exception ex)
        {
            if (e.Args.Contains("--import-only"))
            {
                try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "import-error.txt"), ex.Message); } catch { }
                IsFatalShutdown = true; Shutdown(1); return;
            }
            FatalError("Не удалось запустить ВанБаз", ex);
        }
    }
    private void FatalError(string title, Exception error)
    {
        IsFatalShutdown = true;
        if (presentingFatalError) { Shutdown(1); return; }
        presentingFatalError = true;
        try
        {
            string executable = Path.Combine(AppContext.BaseDirectory, "VanBaz.exe");
            bool canRestart = startupData != null && File.Exists(executable);
            string action = ErrorDialog.Present(title, error, MainWindow, canRestart, fatal: true);
            RestartStartInfo = action == "restart" && canRestart
                ? MainWindow is VanBaz.MainWindow main ? main.FailureRestartPlan()
                    : RestartPlan.Create(executable, startupData!, startupMode, startupOptions?.LockMode == true) : null;
        }
        finally { Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        bridge?.Dispose();
        if (ownsMutex) catalogMutex?.ReleaseMutex();
        catalogMutex?.Dispose();
        base.OnExit(e);
        if (RestartStartInfo is { } restart)
        {
            try { System.Diagnostics.Process.Start(restart); }
            catch (Exception ex) { MessageBox.Show("Не удалось перезапустить ВанБаз. Запустите программу вручную.\n\n" + ex.Message, "Перезапуск ВанБаз", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}
