using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VanBaz;

public static class UiIcons
{
    public static ImageSource Drawing(string key)
    {
        var group = new DrawingGroup();
        void Shape(string path, string color, bool fill = false) => group.Children.Add(new GeometryDrawing(fill ? (Brush)new BrushConverter().ConvertFromString(color)! : null, fill ? null : new Pen((Brush)new BrushConverter().ConvertFromString(color)!, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Geometry.Parse(path)));
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        switch (key)
        {
            case "+": Shape("M8,2 L8,14 M2,8 L14,8", "#379C6A"); break;
            case "▰": case "folder": case "folder-open": case "folder-add":
                Shape("M1,3 L6,3 L8,5 L14,5 L14,13 L1,13 Z", "#BA8A2F", true);
                Shape(key == "folder-open" ? "M1,7 L15,7 L12,14 L0,14 Z" : "M1,6 L14,6 L14,13 L1,13 Z", "#F2CC68", true); if(key == "folder-add") Shape("M11,8 L11,15 M7.5,11.5 L14.5,11.5", "#328956"); break;
            case "tree-open": case "tree-closed": Shape("M3,3 L13,3 L13,13 L3,13 Z M5,8 L11,8", "#73838D"); if(key == "tree-closed") Shape("M8,5 L8,11", "#73838D"); break;
            case "edit": Shape("M2,11 L11,2 L14,5 L5,14 L2,14 Z M10,3 L13,6", "#548DB8"); break;
            case "info": Shape("M8,1 A7,7 0 1 1 7.99,1 M8,7 L8,12 M8,4 L8,4.5", "#548DB8"); break;
            case "move": Shape("M1,5 L8,5 L8,2 L15,8 L8,14 L8,11 L1,11 Z", "#379C85"); break;
            case "copy": Shape("M2,2 L10,2 L10,12 L2,12 Z M6,5 L14,5 L14,15 L6,15 Z", "#548DB8"); break;
            case "lock": Shape("M3,7 L13,7 L13,14 L3,14 Z", "#DCA447", true); Shape("M5,7 L5,4 A3,3 0 0 1 11,4 L11,7 M8,10 L8,12", "#737D86"); break;
            case "history": Shape("M8,1 A7,7 0 1 1 7.99,1 M8,4 L8,8 L11,10", "#548DB8"); break;
            case "settings": Shape("M2,4 L14,4 M2,8 L14,8 M2,12 L14,12 M5,2 L5,6 M11,6 L11,10 M6,10 L6,14", "#548DB8"); break;
            case "system": Shape("M2,2 L6,2 L6,6 L2,6 Z M10,2 L14,2 L14,6 L10,6 Z M2,10 L6,10 L6,14 L2,14 Z M10,10 L14,10 L14,14 L10,14 Z", "#548DB8"); break;
            case "feedback": Shape("M1,3 L15,3 L15,13 L1,13 Z M1,3 L8,9 L15,3", "#548DB8"); break;
            case "help": Shape("M6,4 C6,0 13,1 12,5 C12,7 8,7 8,10 M8,13 L8,14", "#548DB8"); break;
            case "search": Shape("M7,1 A5,5 0 1 1 6.99,1 M11,11 L15,15", "#548DB8"); break;
            case "process": Shape("M2,2 L14,2 L14,12 L2,12 Z M5,15 L11,15 M8,12 L8,15 M4,8 L6,8 L7,5 L9,10 L10,7 L12,7", "#548DB8"); break;
            case "×": Shape("M4,4 L12,12 M4,12 L12,4", "#DC635A"); break;
            case "▣": Shape("M3,2 L11,2 L14,5 L14,14 L3,14 Z M11,2 L11,5 L14,5 M6,8 L11,8 M6,11 L10,11", "#548DB8"); break;
            case "⟳": case "↻": Shape("M13,6 A5,5 0 1 0 13,11 M13,2 L13,6 L9,6", "#379C85"); break;
            case "⌫": Shape("M4,10 L10,4 L13,7 L7,13 Z M3,11 L6,14 M9,5 L12,2", "#C59247"); break;
            case "★": Shape("M8,1 L10,6 L15,6 L11,9 L13,15 L8,12 L3,15 L5,9 L1,6 L6,6 Z", "#DCA447", true); break;
            case "▤": Shape("M2,4 L12,4 L12,14 L2,14 Z M5,1 L15,1 L15,11 M4,7 L10,7 M4,10 L10,10", "#548DB8"); break;
            case "⊟": case "⊞": Shape("M2,2 L14,2 L14,14 L2,14 Z M5,8 L11,8", "#6F9B79"); if (key == "⊞") Shape("M8,5 L8,11", "#6F9B79"); break;
            case "▶": Shape("M4,2 L13,8 L4,14 Z", "#379C6A", true); break;
            case "▧": Shape("M3,2 L11,2 L14,5 L14,14 L3,14 Z M5,12 L11,6 M5,12 L7,12", "#548DB8"); break;
            case "file": Shape("M2,4 C2,1 14,1 14,4 L14,12 C14,15 2,15 2,12 Z", "#E8BF5D", true); Shape("M2,4 C2,7 14,7 14,4 M2,8 C2,11 14,11 14,8", "#AB8535"); break;
            case "web": Shape("M8,1 A7,7 0 1 1 7.99,1 M1,8 L15,8 M8,1 C3,4 3,12 8,15 C13,12 13,4 8,1", "#379C85"); break;
            case "server": Shape("M2,2 L14,2 L14,6 L2,6 Z M2,10 L14,10 L14,14 L2,14 Z M4,4 L5,4 M4,12 L5,12", "#548DB8"); break;
            default: Shape("M2,5 L14,5 L14,11 L2,11 Z", "#548DB8", true); break;
        }
        var image = new DrawingImage(group); image.Freeze(); return image;
    }
    // Explicit command identities keep icons stable when captions are renamed or localized.
    public static Image MenuIcon(string caption, string command = "") => Icon(command switch
    {
        "AddGroup_Click" => "folder-add", "Add_Click" or "AddCommand_Click" => "+",
        "Edit_Click" or "EditCommand_Click" or "RenameBase_Click" or "RenameGroup_Click" => "edit",
        "Summary_Click" => "info", "MoveToGroup_Click" => "move",
        "CopySummary_Click" or "CopyConnection_Click" or "Duplicate_Click" => "copy",
        "Delete_Click" or "DeleteCommand_Click" or "Exit_Click" => "×",
        "Enterprise_Click" or "Starter_Click" or "WebClient_Click" => "▶",
        "Designer_Click" => "▧", "Favorite_Click" => "★",
        "SessionLock_Click" or "Lock_Click" => "lock",
        "BaseFolder_Click" or "DataFolder_Click" => "folder-open",
        "Refresh_Click" or "ReloadOfficialLists_Click" or "CheckUpdates_Click" or "ActualReleases_Click" => "↻",
        "Cache_Click" or "LegacyIndexes_Click" => "⌫",
        "Settings_Click" or "Themes_Click" or "AssignVersion_Click" => "settings",
        "Help_Click" or "About_Click" => "help",
        "SnapshotList_Click" or "Snapshot_Click" => "▤",
        "Collapse_Click" => "⊟", "Expand_Click" => "⊞",
        _ => CaptionIcon(caption)
    });
    private static string CaptionIcon(string caption) => caption.TrimEnd('…') switch
    {
        "Списки информационных баз" or "Снимки каталога" => "▤",
        "Найти файловые базы" => "search",
        "Кэш и файлы профиля" => "⌫",
        "Процессы 1С" => "process",
        "Установленные платформы" => "server",
        "Релизы конфигураций" => "↻",
        "Закладки" => "★",
        "Инструменты ВанБаз" or "Профили запуска" => "settings",
        "Помощь" => "help",
        "Копировать" or "Копировать путь" or "Копировать строку соединения" or "Наименование" or "ID базы" or "Строку подключения" or "Адрес / каталог" or "Версию платформы" or "Сводную информацию" or "Копировать версию" => "copy",
        "Сводная информация" => "info",
        "Добавить базу" or "Новая команда" => "+", "Добавить группу" => "folder-add",
        "Добавить в избранное" or "Убрать из избранного" => "★",
        "Свойства" or "Изменить" or "Изменить каталог" or "Переименовать" or "Вставить версию" => "edit",
        "Открыть каталог" or "Открыть папку базы" or "Папка данных ВанБаз" => "folder-open",
        "Обслуживание" or "Блокировка входа" or "Блокировка входа в базу" or "Временно заблокировать" => "lock",
        "Настройки" or "Темы" or "Колонки списка" or "Режим работы" or "Каталоги платформ и браузер" => "settings",
        "Обновления" or "Перечитать штатный список" or "Проверить релиз базы" or "Актуальные релизы" => "↻",
        "Выход" or "Удалить из списка" or "Удалить группу" or "Удалить" => "×",
        "Клавиши и действия" or "О программе" => "help",
        "1С:Предприятие" or "Обычный запуск стартера" or "Открыть веб-клиент в браузере" or "Запуск по клавишам" => "▶",
        "Конфигуратор" => "▧", "История" => "history",
        "Сохранённые снимки" or "Сохранить снимок каталога" or "Восстановить снимок" => "▤",
        "Переместить в группу" => "move", "Свернуть / развернуть" => "⊞",
        "Кэш 1С и карантин" => "⌫", _ => "▣"
    };
    public static Image Icon(string key) => new() { Source = Drawing(key), Width = 16, Height = 16, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
    public static StackPanel Label(string key, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal }; var icon = Icon(key); icon.Margin = new(0, 0, 4, 0); panel.Children.Add(icon); panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }); return panel;
    }
}
