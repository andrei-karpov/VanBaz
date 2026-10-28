using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow
{
    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || Selected is not { } entry) return;
        if (entry.Kind == "Web") { MessageBox.Show("Укажите прямое файловое или серверное подключение."); return; }
        var f = new FormDialog("Установить обновление · " + entry.Name, 810, 600) { Owner = this };
        f.Section("Обновление"); f.BrowseText("file", "CFU / CF", "", false, "Обновление конфигурации|*.cfu;*.cf");
        f.BrowseText("merge", "Настройки объединения", "", false, "Настройки объединения|*.xml");
        f.Check("database", "После объединения обновить конфигурацию базы данных", false);
        f.Note("ВанБаз не создаёт резервных копий. Перед обновлением сохраните базу средствами 1С. Для изменённой конфигурации используйте согласованные настройки объединения XML. ZIP-дистрибутив предварительно распакуйте.");
        f.Note("Закройте сеансы базы и проверьте допустимый исходный релиз. Обновление конфигурации БД может менять структуру и данные. При ошибке автоматический откат не выполняется; журнал сохраняется.");
        f.Check("confirm", "Резервная копия создана отдельно; подтверждаю обновление базы: " + entry.Name, false);
        f.SetAcceptText("Обновить");
        f.Validate = () => { if (!f.Flag("confirm")) return f.SetError("Подтвердите выбранную базу."); LaunchService.BuildUpdate(entry, f.Value("file"), f.Value("merge"), f.Flag("database"), "preview.log", platforms); return true; };
        if (f.ShowDialog() != true || isolatedSession) return;
        if (!operations.Add(entry.Id)) { MessageBox.Show("Операция с базой уже выполняется."); return; }
        string logs = Path.Combine(store.Folder, "Logs"); string log = Path.Combine(logs, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-update.log");
        try
        {
            Directory.CreateDirectory(logs);
            StatusText.Text = "Обновление конфигурации: " + entry.Name;
            using var process = LaunchService.Start(LaunchService.BuildUpdate(entry, f.Value("file"), f.Value("merge"), f.Flag("database"), log, platforms));
            await process.WaitForExitAsync();
            string result = process.ExitCode == 0 ? "Платформа завершила обновление с кодом 0." : "Обновление требует проверки. Код платформы: " + process.ExitCode + ". Автоматический откат не выполнялся.";
            StatusText.Text = result;
            FormDialog.ShowText(this, "Результат обновления", result + "\nЖурнал: " + log + "\n\n" + (File.Exists(log) ? V8iImporter.ReadText(log) : "Платформа не создала журнал."));
        }
        catch (Exception ex) { Error("Обновление конфигурации", ex); }
        finally { operations.Remove(entry.Id); }
    }
    private void DeleteBaseFile(BaseEntry entry)
    {
        if (operations.Count > 0) { MessageBox.Show("Дождитесь завершения операций."); return; }
        var f = CreateDeletionDialog(entry); if (f.ShowDialog() != true) return;
        bool file = f.Flag("file"), cache = f.Flag("cache");
        if (!file && !cache) { Commit(() => catalog.Bases.Remove(entry)); return; }
        if (isolatedSession) return;
        try
        {
            if (cache && CacheService.ClientsRunning()) throw new IOException("Закройте процессы 1С перед очисткой кэша. Запись не удалена.");
            var cacheErrors = new List<string>();
            var folders = cache && Guid.TryParse(entry.Id, out var entryId) ? CacheService.Find(CacheService.ProfileRoots(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), catalog.Bases, false, cacheErrors).Where(item => Guid.TryParse(item.BaseId, out var id) && id == entryId).ToArray() : Array.Empty<CacheFolder>();
            if (file)
            {
                string full = Path.GetFullPath(entry.Location).TrimEnd('\\');
                if (catalog.Bases.Any(b => b.Id != entry.Id && b.Kind == "File" && Path.GetFullPath(b.Location).TrimEnd('\\').Equals(full, StringComparison.OrdinalIgnoreCase))) throw new IOException("На файл ссылается другая запись. Сначала устраните дубликаты подключений.");
            }
            store.Save(catalog); store.Snapshot();
            var record = file ? DeletedBaseService.Quarantine(entry, Path.Combine(store.Folder, "DeletedBases")) : null;
            if (!Commit(() => catalog.Bases.Remove(entry))) { if (record != null) DeletedBaseService.Restore(record); throw new IOException("Каталог не сохранён. Удаление отменено, файл возвращён на место."); }
            var results = new List<string> { "Запись удалена из списка." }; if (record != null) results.Add("Файл в карантине: " + record.StoredFile);
            foreach (var folder in folders) try { if (CacheService.ClientsRunning()) throw new IOException("Появился процесс 1С."); CacheService.Move(folder, false); results.Add("Кэш в карантине: " + folder.Path); } catch (Exception ex) { results.Add("Кэш не изменён: " + folder.Path + " · " + ex.Message); }
            if (cache && folders.Length == 0 && cacheErrors.Count == 0) results.Add("Кэш этой базы в профиле текущего пользователя не найден.");
            results.AddRange(cacheErrors.Select(error => "Кэш не проверен: " + error));
            StatusText.Text = string.Join(" ", results); FormDialog.ShowText(this, "Результат удаления", string.Join("\n", results));
        }
        catch (Exception ex) { Error("Удаление базы", ex); }
    }
    private FormDialog CreateDeletionDialog(BaseEntry entry)
    {
        var f = new FormDialog("Удаление базы · " + entry.Name, 780, 460) { Owner = this }; f.Section("Удаление");
        f.Note("Запись будет удалена из списка ВанБаз.");
        f.Check("file", "Также убрать файл 1Cv8.1CD из каталога базы", false);
        f.SetEnabled("file", InterfaceMode == "Разработчик" && entry.Kind == "File" && entry.Version != "7.7");
        f.Check("cache", "Также убрать кэш базы в Local / Roaming текущего пользователя", false); f.SetEnabled("cache", entry.Version != "7.7");
        f.Note("Файл перемещается в .VanBaz-Deleted рядом с папкой базы. Его можно вернуть через «Восстановить удалённый файл базы». Место на диске освобождается только после удаления этой копии вручную. Остальные файлы каталога сохраняются.");
        f.Text("confirm", "Для удаления файла введите имя базы", "");
        f.Validate = () => !f.Flag("file") || f.Value("confirm") == entry.Name || f.SetError("Введите точное имя базы.");
        f.Note("Кэш перемещается в карантин; возврат доступен в окне очистки кэша. Другие Windows-профили здесь не обрабатываются. При частичной ошибке будет показан результат каждого шага.");
        f.SetAcceptText("Удалить"); return f;
    }
    private void RestoreDeletedBase_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || InterfaceMode != "Разработчик" || isolatedSession || operations.Count > 0) return;
        string folder = Path.Combine(store.Folder, "DeletedBases");
        if (!Directory.Exists(folder)) { MessageBox.Show("Сохранённых удалений пока нет."); return; }
        var dialog = new OpenFileDialog { Title = "Запись удаления базы", InitialDirectory = folder, Filter = "Запись удаления|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 65536) throw new IOException("Некорректный размер записи.");
            var record = JsonSerializer.Deserialize<DeletedBase>(File.ReadAllText(dialog.FileName)) ?? throw new IOException("Повреждённая запись удаления.");
            if (MessageBox.Show("Вернуть файл базы «" + record.BaseName + "»?\n" + record.OriginalFile + "\nСуществующий файл заменяться не будет.", "Восстановление", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            DeletedBaseService.Restore(record);
            StatusText.Text = "Файл восстановлен. Для возврата записи используйте снимок каталога или добавьте базу.";
            _ = RefreshMetadata();
        }
        catch (Exception ex) { Error("Возврат файла базы", ex); }
    }
}
