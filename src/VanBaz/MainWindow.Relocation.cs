using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public partial class MainWindow
{
    private async void ConfirmExternalFormatting(ExternalRelocationPlan plan)
    {
        if (plan.Changes.Count == 0) { StatusText.Text = "Наименования уже соответствуют правилу."; return; }
        var review = new FormDialog("Форматирование отдельного списка", 850, 550) { Owner = this }; review.Section("Изменения");
        review.Note("Файл: " + plan.Path);
        review.Element(new DataGrid { ItemsSource = plan.Changes.Select(c => new { До = c.Before, После = c.After }), AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, Height = 260 });
        review.Check("write", "Разрешаю изменить выбранный .v8i по этому списку", false);
        review.Note("Сохраняются идентификаторы, подключения, комментарии и неизвестные поля. Копия исходного файла останется рядом, операция попадёт в журнал восстановления.");
        review.SetAcceptText("Применить"); review.Validate = () => review.Flag("write") || review.SetError("Подтвердите изменение файла.");
        if (review.ShowDialog() != true || isolatedSession || !CanEdit) return;
        try
        {
            var result = ExternalListRelocation.Apply(plan, store, review.Flag("write"));
            if (useOfficialLists && (CatalogReconciler.IsStandardList(plan.Path) || officialLists?.Sources.Contains(plan.Path, StringComparer.OrdinalIgnoreCase) == true)) await RefreshOfficialListsAsync();
            StatusText.Text = "Отформатировано записей: " + plan.Changes.Count + ". Копия: " + result.Backup + (result.Warning == null ? "" : ". " + result.Warning);
        }
        catch (Exception ex) { Error("Форматирование отдельного списка", ex); }
    }
    private async void ShowRelocation()
    {
        if (!CanEdit) return;
        try
        {
            var form = new FormDialog("Замена пути файловых баз", 780, 510) { Owner = this }; form.Section("Пути");
            var source = form.Choice("source", "Список баз", "Текущий каталог", new[] { "Текущий каталог", "Отдельный файл .v8i" });
            form.BrowseText("file", "Файл списка", "", false, "Список баз 1С|*.v8i"); form.SetEnabled("file", false);
            source.SelectionChanged += (_, _) => form.SetEnabled("file", form.Value("source") == "Отдельный файл .v8i");
            form.Text("old", "Старый диск / каталог", ""); form.Text("new", "Новый диск / каталог", "");
            form.Note("Меняются адреса подключений. Файлы самих баз не перемещаются. Для текущего каталога общие регистрации пропускаются; отдельный файл изменяется только после подтверждения.");
            form.SetAcceptText("Предпросмотр");
            form.Validate = () =>
            {
                if (form.Value("source") == "Отдельный файл .v8i")
                    return ExternalListRelocation.Prepare(form.Value("file"), form.Value("old"), form.Value("new")).Changes.Count > 0 || form.SetError("Подходящих файловых баз нет.");
                return CurrentChanges().Count > 0 || form.SetError("Подходящих личных файловых баз нет.");
            };
            if (form.ShowDialog() != true) return;
            bool external = form.Value("source") == "Отдельный файл .v8i";
            var plan = external ? ExternalListRelocation.Prepare(form.Value("file"), form.Value("old"), form.Value("new")) : null;
            var changes = plan?.Changes ?? CurrentChanges();
            var review = new FormDialog("Подтвердить замену путей", 850, 590) { Owner = this }; review.Section("Изменения");
            review.Note(external ? "Будет изменён файл: " + plan!.Path : "Личные подключения текущего каталога; общие источники не изменяются.");
            var text = review.Text("preview", "Подключения: " + changes.Count, string.Join("\n\n", changes.Select(c => c.Name + "\n" + c.Before + "\n→ " + c.After)), true);
            text.IsReadOnly = true; text.Height = 260; text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            review.Check("confirmed", external ? "Разрешаю изменить выбранный файл .v8i по этому списку" : "Подтверждаю изменение перечисленных подключений", false);
            review.Note(external ? "Сохраняются кодировка, комментарии и неизвестные поля. Перед заменой создаются копия .bak и запись журнала восстановления. Доступность новых каталогов не проверяется."
                : "Перед сохранением создаётся снимок каталога. Доступность новых каталогов не проверяется.");
            review.Validate = () => review.Flag("confirmed") || review.SetError("Подтвердите список изменений."); review.SetAcceptText("Заменить пути");
            if (review.ShowDialog() != true || isolatedSession) return;
            if (plan != null)
            {
                var result = ExternalListRelocation.Apply(plan, store, review.Flag("confirmed"));
                if (useOfficialLists && (CatalogReconciler.IsStandardList(plan.Path) || officialLists?.Sources.Contains(plan.Path, StringComparer.OrdinalIgnoreCase) == true)) await RefreshOfficialListsAsync();
                StatusText.Text = "Обновлено подключений в .v8i: " + changes.Count + ". Копия: " + result.Backup + (result.Warning == null ? "" : ". " + result.Warning);
            }
            else
            {
                if (changes.Any(c => catalog.Bases.FirstOrDefault(b => b.Id == c.Id)?.Location != c.Before)) throw new IOException("Каталог изменился после предпросмотра. Повторите замену путей.");
                store.Save(catalog); store.Snapshot();
                if (Commit(() => { foreach (var change in changes) catalog.Bases.First(b => b.Id == change.Id).Location = change.After; }))
                { _ = RefreshMetadata(); StatusText.Text = "Обновлено подключений: " + changes.Count; }
            }
            List<BasePathChange> CurrentChanges()
            {
                var local = catalog.Copy(); local.Bases.RemoveAll(b => officialLists?.IsCommon(b.Id) == true);
                return CatalogRelocation.Preview(local, form.Value("old"), form.Value("new"));
            }
        }
        catch (Exception ex) { Error("Замена путей", ex); }
    }
}
