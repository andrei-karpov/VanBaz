using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public sealed class NewBaseSelectionWindow : Window
{
    public string SelectedKind { get; private set; }
    public string SelectedVersion { get; private set; } = "";
    public TextBox VersionBox { get; }
    public Button ContinueButton { get; }

    public NewBaseSelectionWindow(string kind, string version, bool legacyEnabled, bool creating = false)
    {
        SelectedKind = kind is "Server" or "Web" ? kind : "File";
        if (creating && SelectedKind == "Web") SelectedKind = "File";
        Title = creating ? "Создание информационной базы" : "Регистрация информационной базы";
        Width = 570; MinWidth = 440; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; DialogBehavior.FitToScreen(this);
        var root = new StackPanel { Margin = new(16) };
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock { Text = "Размещение информационной базы", FontWeight = FontWeights.Bold, Margin = new(0, 0, 0, 12) });
        var locations = new WrapPanel(); root.Children.Add(locations);
        var platformButtons = new List<Button>();
        void UpdateLegacy() { foreach (var button in platformButtons) if ((string)button.Tag == "7.7") button.IsEnabled = legacyEnabled && SelectedKind == "File"; }
        foreach (var item in new[] { ("File", "Компьютер", "file"), ("Server", "Сервер 1С", "server"), ("Web", "Web-сервер", "web") })
        {
            if (creating && item.Item1 == "Web") continue;
            var radio = new RadioButton { Name = "NewBase" + item.Item1, Content = UiIcons.Label(item.Item3, item.Item2), GroupName = "NewBaseLocation", MinWidth = 153, Margin = new(0, 4, 8, 10), IsChecked = SelectedKind == item.Item1 };
            string value = item.Item1;
            radio.Checked += (_, _) => { SelectedKind = value; UpdateLegacy(); };
            locations.Children.Add(radio);
        }
        root.Children.Add(new TextBlock { Text = creating ? "Создать для платформы" : "Создать запись для платформы", FontWeight = FontWeights.Bold, Margin = new(0, 16, 0, 8) });
        var versions = new WrapPanel(); root.Children.Add(versions);
        var status = new TextBlock { Name = "NewBaseSelectionStatus", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "ThemeError");
        VersionBox = new TextBox { Name = "NewBaseVersion", Text = version, MinWidth = 150, ToolTip = "Семейство платформы (8.3) или точная версия (8.3.27.1606)" };
        void Accept(string selected)
        {
            selected = selected.Trim();
            string parsedText = selected.TrimEnd('*', '.');
            if (!Version.TryParse(parsedText, out var parsed) || parsed.Major is not (7 or 8) || parsed.Major == 7 && parsed.Minor != 7)
            { status.Text = "Укажите семейство 8.x, точную версию 8.x.x.x или 7.7."; VersionBox.Focus(); return; }
            if (parsed.Major == 7 && (creating || !legacyEnabled || SelectedKind != "File"))
            { status.Text = creating ? "Создание базы поддерживается для платформы 8.x." : "Платформа 7.7 доступна только для файловой базы при включённой поддержке 7.7."; return; }
            SelectedVersion = selected; DialogResult = true;
        }
        foreach (string family in new[] { "8.5", "8.4", "8.3", "8.2", "8.1", "7.7" })
        {
            if (family == "7.7" && (!legacyEnabled || creating)) continue;
            var button = new Button { Name = "NewBaseVersion" + family.Replace(".", ""), Content = family, Tag = family, MinWidth = 73, MinHeight = 38, ToolTip = "Продолжить с платформой " + family };
            button.Click += (_, _) => Accept(family); versions.Children.Add(button); platformButtons.Add(button);
        }
        UpdateLegacy();
        root.Children.Add(new TextBlock { Text = "Другая версия", Margin = new(0, 14, 0, 5) });
        root.Children.Add(VersionBox);
        root.Children.Add(new TextBlock { Text = creating ? "Далее можно выбрать параметры пустой базы или установленный шаблон." : "Далее укажите подключение к существующей базе. Её файлы не создаются.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 14, 0, 0) });
        root.Children.Add(status);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) }; root.Children.Add(footer);
        ContinueButton = new Button { Name = "ContinueNewBase", Content = "Далее →", MinWidth = 100, IsDefault = true };
        ContinueButton.Click += (_, _) => Accept(VersionBox.Text); footer.Children.Add(ContinueButton);
        var cancel = new Button { Name = "CancelNewBase", Content = "Отмена", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close(); footer.Children.Add(cancel);
    }
}
