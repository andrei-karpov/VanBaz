using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public partial class MainWindow
{
    private void Bookmarks_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || e.OriginalSource != menu) return;
        menu.Items.Clear();
        for (int slot = 1; slot <= 9; slot++)
        {
            int number = slot; catalog.Settings.Bookmarks.TryGetValue(slot, out string? id);
            var entry = catalog.Bases.FirstOrDefault(b => b.Id == id);
            var item = new MenuItem { Header = slot + " · " + (entry?.Name ?? (id == null ? "Свободно" : "База отсутствует")), Icon = UiIcons.Icon("★") };
            MenuItem Action(string caption, RoutedEventHandler action, bool enabled)
            { var command = new MenuItem { Header = caption, IsEnabled = enabled, Icon = UiIcons.MenuIcon(caption) }; command.Click += action; item.Items.Add(command); return command; }
            Action("Перейти", (_, _) => { if (entry != null) SelectBase(entry.Id); }, entry != null && !locked);
            Action("1С:Предприятие", async (_, _) => { if (entry != null && SelectBaseInList(entry.Id)) await LaunchAsync(false); }, entry != null && !locked && operations.Count == 0);
            Action("Конфигуратор", async (_, _) => { if (entry != null && SelectBaseInList(entry.Id)) await LaunchAsync(true); }, entry != null && entry.Kind != "Web" && CanEdit);
            item.Items.Add(new Separator());
            Action("Назначить выбранную базу", (_, _) => { if (Selected is { } selected) Commit(() => catalog.Settings.Bookmarks[number] = selected.Id); }, Selected != null && !locked && operations.Count == 0);
            Action("Очистить", (_, _) => Commit(() => catalog.Settings.Bookmarks.Remove(number)), id != null && !locked && operations.Count == 0);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Очистить все закладки", Icon = UiIcons.Icon("×"), IsEnabled = !locked && operations.Count == 0 && catalog.Settings.Bookmarks.Count > 0 };
        clear.Click += (_, _) => { if (MessageBox.Show(this, "Очистить девять закладок? Базы сохранятся.", "Закладки", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) Commit(() => catalog.Settings.Bookmarks.Clear()); };
        menu.Items.Add(clear);
    }
}
