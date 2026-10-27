using System.IO;
using System.Windows;

namespace VanBaz;

public partial class MainWindow
{
    private bool useOfficialLists;
    private OfficialListSession? officialLists;
    private string officialListError = "";
    private Task<OfficialListSession>? officialRead;
    private async Task RefreshOfficialListsAsync()
    {
        if (!useOfficialLists || HasPendingCatalogRecovery()) return;
        const string operation = "official-list-read";
        if (!operations.Add(operation)) return;
        try
        {
            if (officialRead == null || officialRead.IsCompleted) officialRead = Task.Run(() => OfficialListSession.Load());
            var session = await officialRead.WaitAsync(TimeSpan.FromSeconds(15));
            var refreshed = session.Refresh(catalog);
            if (File.Exists(store.FilePath)) store.Snapshot();
            store.Save(refreshed); catalog = refreshed; officialLists = session; officialListError = ""; BuildRows();
            int local = catalog.Bases.Count(b => b.Version != "7.7" && !session.Contains(b.Id));
            StatusText.Text = "Список 1С обновлён. Источников: " + session.Sources.Count + (local == 0 ? "" : ". Местных записей прежнего каталога: " + local);
        }
        catch (Exception ex)
        {
            officialLists = null; officialListError = ex is TimeoutException ? "Источник не ответил за 15 секунд. Проверьте доступ к общему списку и нажмите F5." : ex.Message;
            StatusText.Text = "Список 1С не обновлён: " + officialListError + ". Сохранён прежний каталог. Подробности: Списки баз → Штатные источники.";
        }
        finally { operations.Remove(operation); }
    }
    private FormDialog CreateOfficialListsDialog()
    {
        var f = new FormDialog("Штатные источники и личные настройки", 900, 620); f.Section("Список 1С");
        var path = f.Text("source", "Пользовательский список", officialLists?.UserList ?? CatalogReconciler.StandardList); path.IsReadOnly = true;
        f.Note("Добавление базы, изменение подключения, группы и порядка сохраняются в пользовательском ibases.v8i при выполнении действия. Избранное, заметки, пароли, история и оформление остаются в ВанБаз.");
        var sources = f.Text("sources", "Обнаруженные источники", officialLists == null ? officialListError.Length == 0 ? "Список ещё не прочитан." : officialListError : string.Join("\n", officialLists.Sources), true);
        sources.IsReadOnly = true; sources.Height = 105;
        f.Note("Общие списки из CommonInfoBases читаются из системного и пользовательского 1cestart.cfg. Обычное редактирование не меняет корпоративные файлы. Личная группа и порядок общей базы записываются в пользовательский список с External=1. Её имя и подключение задаёт общий источник.");
        int local = officialLists == null ? 0 : catalog.Bases.Count(b => b.Version != "7.7" && !officialLists.Contains(b.Id));
        f.Note("F5 перечитывает списки. При конфликте с другим приложением сохранение останавливается и предлагает обновить список. Снимки и возврат доступны через сервисное меню.");
        f.Note("Записи прежнего каталога, которых нет в источниках: " + local + ". Они сохранены локально. Их можно передать через «Согласовать со списком 1С»; изменение такой записи также регистрирует её в штатном списке. При первом чтении штатные свойства существующих ID берутся из 1С; прежний каталог сохраняется снимком.");
        f.SetAcceptText("Закрыть"); return f;
    }
    private void OfficialLists_Click(object sender, RoutedEventArgs e)
    { var f = CreateOfficialListsDialog(); f.Owner = SettingsOwner; f.ShowDialog(); }
    private async void ReloadOfficialLists_Click(object sender, RoutedEventArgs e)
    { if (operations.Count == 0) await RefreshOfficialListsAsync(); }
}
