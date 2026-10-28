using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public partial class MainWindow
{
    private void MoveToGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || Selected is not { } entry) return;
        var dialog = new GroupSelectionWindow(catalog.Groups.Select(g => g.Path).Concat(catalog.Bases.Select(b => b.Group)), entry.Group, "Переместить в группу: " + entry.Name) { Owner = this };
        dialog.SelectButton.Content = "Переместить";
        if (dialog.ShowDialog() == true && Commit(() => entry.Group = dialog.SelectedGroup)) SelectBase(entry.Id);
    }
    private void AssignVersion_Click(object sender, RoutedEventArgs e)
    {
        if (!Enabled("v8") || Selected is not { } entry) return;
        if (officialLists?.IsCommon(entry.Id) == true) { StatusText.Text = "Версию общей регистрации изменяют в её исходном списке."; return; }
        var dialog = new PlatformVersionWindow(platforms, entry.Version, entry.Architecture, entry.Name) { Owner = this };
        if (dialog.ShowDialog() == true) Commit(() => { entry.Version = dialog.SelectedVersion; entry.Architecture = dialog.SelectedArchitecture; entry.Executable = ""; });
    }
    public static string FormatName(string value, string mode, bool trim, string from, string to)
    {
        if (trim) value = value.Trim();
        if (from.Length > 0) value = value.Replace(from, to, StringComparison.CurrentCulture);
        var culture = CultureInfo.CurrentCulture;
        return mode switch { "Все буквы ЗАГЛАВНЫЕ" => value.ToUpper(culture), "Все буквы строчные" => value.ToLower(culture), "Первая буква Заглавная" => value.Length == 0 ? value : char.ToUpper(value[0], culture) + value[1..].ToLower(culture), "Первая буква каждого слова Заглавная" => culture.TextInfo.ToTitleCase(value.ToLower(culture)), _ => value };
    }
    private void FormatNames_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        var dialog = new FormDialog("Форматирование наименований", 730, 540) { Owner = this };
        dialog.Section("Правила");
        var source = dialog.Choice("source", "Список баз", "Текущий каталог", new[] { "Текущий каталог", "Отдельный файл .v8i" });
        dialog.BrowseText("file", "Файл списка", "", false, "Список баз 1С|*.v8i"); dialog.SetEnabled("file", false);
        source.SelectionChanged += (_, _) => dialog.SetEnabled("file", dialog.Value("source") == "Отдельный файл .v8i");
        dialog.Choice("mode", "Регистр", "Не форматировать", new[] { "Не форматировать", "Все буквы ЗАГЛАВНЫЕ", "Все буквы строчные", "Первая буква Заглавная", "Первая буква каждого слова Заглавная" });
        dialog.Check("trim", "Убрать концевые пробелы", true); dialog.Check("bases", "Информационные базы", true); dialog.Check("groups", "Группы", false);
        dialog.Text("from", "Заменить строку", ""); dialog.Text("to", "На строку", ""); dialog.SetAcceptText("Предпросмотр");
        if (dialog.ShowDialog() != true) return;
        try
        {
            string Convert(string value) => FormatName(value, dialog.Value("mode"), dialog.Flag("trim"), dialog.Value("from"), dialog.Value("to"));
            if (dialog.Value("source") == "Отдельный файл .v8i")
            {
                var plan = ExternalListRelocation.PrepareNames(dialog.Value("file"), Convert, dialog.Flag("bases"), dialog.Flag("groups"));
                ConfirmExternalFormatting(plan); return;
            }
            var commonGroups = catalog.Groups.Where(g => officialLists?.IsCommon(g.Id) == true).Select(g => g.Path).ToArray();
            string Group(string path) => commonGroups.Any(p => InGroup(path, p) || InGroup(p, path)) ? path : string.Join('/', path.Split('/').Select(Convert));
            var names = catalog.Bases.Where(b => dialog.Flag("bases") && officialLists?.IsCommon(b.Id) != true && b.Name != Convert(b.Name)).ToDictionary(b => b.Id, b => Convert(b.Name));
            var groups = catalog.Groups.Select(g => g.Path).Concat(catalog.Bases.Select(b => b.Group)).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, p => dialog.Flag("groups") ? Group(p) : p, StringComparer.OrdinalIgnoreCase);
            if (names.Values.Any(n => string.IsNullOrWhiteSpace(n) || n.IndexOfAny(new[] { '\r', '\n', ']' }) >= 0) || groups.Values.Any(n => n.Split('/').Any(part => string.IsNullOrWhiteSpace(part)) || n.IndexOfAny(new[] { '\r', '\n', ']' }) >= 0)) throw new InvalidOperationException("Правило создаёт пустое или недопустимое наименование.");
            if (groups.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != groups.Count) throw new InvalidOperationException("Разные группы получат одинаковые пути. Измените правило.");
            var changes = catalog.Bases.Where(b => names.ContainsKey(b.Id)).Select(b => new { До = b.Name, После = names[b.Id], Тип = "База" }).Concat(groups.Where(g => g.Key != g.Value).Select(g => new { До = g.Key, После = g.Value, Тип = "Группа" })).ToList();
            if (changes.Count == 0) { StatusText.Text = "Наименования уже соответствуют правилу."; return; }
            var review = new FormDialog("Проверка изменений наименований", 760, 500) { Owner = this }; review.Section("Изменения");
            review.Note("Общие регистрации пропущены. Изменения личных записей будут записаны в штатный список.");
            review.Element(new DataGrid { ItemsSource = changes, AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, Height = 280 }); review.SetAcceptText("Применить");
            if (review.ShowDialog() != true) return;
            Commit(() => { foreach (var b in catalog.Bases) { if (names.TryGetValue(b.Id, out var name)) b.Name = name; if (groups.TryGetValue(b.Group, out var path)) b.Group = path; } foreach (var g in catalog.Groups) g.Path = groups.GetValueOrDefault(g.Path, g.Path); });
        }
        catch (Exception ex) { Error("Форматирование списка", ex); }
    }
}
