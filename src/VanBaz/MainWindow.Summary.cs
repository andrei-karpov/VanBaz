using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VanBaz;

public partial class MainWindow
{
    private BaseInfoWindow? CreateSummaryWindow()
    {
        if (locked) return null;
        if (Selected is { } b)
        {
            var fields = new List<InfoField> { new("Наименование", b.Name), new("Идентификатор", b.Id), new("Размещение", b.Location, b.Kind == "File"), new("Подключение", b.ConnectionDisplay), new("Платформа", b.Version), new("Группа", b.Group.Length == 0 ? "Корень списка" : b.Group) };
            foreach (string path in b.ListSources) fields.Add(new("Список баз", path, true));
            fields.Add(new("Тип списка баз", officialLists?.IsCommon(b.Id) == true ? "Общий (подключение только для чтения)" : b.ListSources.Count == 0 ? "Локальный каталог ВанБаз" : "Личный / импортированный"));
            if (b.ConfigurationName.Length > 0) fields.Add(new("Конфигурация", b.ConfigurationName));
            if (b.ConfigurationVersion.Length > 0) fields.Add(new("Релиз", b.ConfigurationVersion));
            if (b.Description.Length > 0) fields.Add(new("Комментарий", b.Description));
            if (b.Notes.Length > 0) fields.Add(new("Примечание", b.Notes));
            return new BaseInfoWindow("Сведения по информационной базе", fields) { Owner = this };
        }
        if (IsRealGroup)
        {
            string path = SelectedGroupPath; var g = catalog.Groups.FirstOrDefault(g => g.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            var fields = new List<InfoField> { new("Наименование", path.Split('/').Last()), new("Идентификатор", g?.Id ?? "Группа задана путями баз"), new("Группа", path), new("Баз в группе", catalog.Bases.Count(b => InGroup(b.Group, path)).ToString()) };
            foreach (string source in g?.ListSources ?? new()) fields.Add(new("Список баз", source, true));
            fields.Add(new("Тип списка баз", g != null && officialLists?.IsCommon(g.Id) == true ? "Общий" : "Личный / локальный"));
            return new BaseInfoWindow("Сведения по группе информационных баз", fields) { Owner = this };
        }
        return null;
    }
    private void Summary_Click(object sender, RoutedEventArgs e) => CreateSummaryWindow()?.ShowDialog();
    private static void AddMenuIcons(ItemsControl menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        { item.Icon ??= UiIcons.MenuIcon(item.Header?.ToString() ?? ""); AddMenuIcons(item); }
    }
    public static string SummaryText(BaseEntry b)
    {
        var text = new StringBuilder();
        void Add(string label, string value) { if (!string.IsNullOrWhiteSpace(value)) text.AppendLine(label + ": " + value); }
        Add("Наименование", b.Name); Add("Комментарий", b.Description); Add("Примечание", b.Notes);
        Add("Подключение", b.ConnectionDisplay); Add("Группа", b.Group);
        foreach (string source in b.ListSources) Add("Список баз", source);
        Add("Платформа", b.Version); Add("Разрядность", b.Architecture); Add("Режим", b.Client);
        Add("Конфигурация", b.ConfigurationName); Add("Релиз", b.ConfigurationVersion);
        Add("Последний запуск", b.LastLaunch?.ToString("dd.MM.yyyy HH:mm") ?? ""); Add("ID", b.Id);
        return text.ToString().TrimEnd();
    }
    private async void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } b || locked) return;
        await CopyTextAsync(SummaryText(b), "Сводная информация скопирована");
    }
    private Task<bool> CopyTextAsync(string text, string message = "Скопировано") =>
        ClipboardService.CopyAsync(this, text, () => StatusText.Text = message, error => StatusText.Text = error);

    private void SummaryMenu_Click(object sender, MouseButtonEventArgs e)
    {
        if (Selected == null || locked) return;
        CopyMenu_Click(sender, e); e.Handled = true;
    }
    private void ConnectionMenu_Click(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not { Kind: "File" } b || locked) return;
        var menu = NewMenu();
        menu.Items.Add(Item("Открыть каталог", BaseFolder_Click, "selected", "Shift+F12"));
        menu.Items.Add(Item("Изменить каталог…", (_, _) => Edit(b, 1), "properties"));
        menu.Items.Add(new Separator());
        foreach (var pair in new[] { ("Копировать путь", b.Location), ("Копировать строку соединения", b.ConnectionDisplay) })
            menu.Items.Add(Item(pair.Item1, async (_, _) => await CopyTextAsync(pair.Item2, "Скопировано: " + pair.Item1), "selected"));
        OpenMenu(ConnectionLink, menu); e.Handled = true;
    }
}
