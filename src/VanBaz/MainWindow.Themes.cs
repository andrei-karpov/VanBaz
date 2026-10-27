using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow
{
    private readonly MenuItem ThemesMenu = new() { Header = "Темы" };
    private void RefreshThemeMenu()
    {
        ThemesMenu.Items.Clear();
        var names = ThemeService.All(catalog.Settings).Select(t => t.Name).ToHashSet();
        foreach (string name in catalog.Settings.FavoriteThemes.Where(names.Contains).Distinct().Take(5))
        {
            var item = new MenuItem { Header = name.Replace("_", "__"), IsCheckable = true, IsChecked = catalog.Settings.Theme == name };
            item.Click += (_, _) => { if (Commit(() => catalog.Settings.Theme = name)) ApplyPreferences(); };
            ThemesMenu.Items.Add(item);
        }
        ThemesMenu.Items.Add(new Separator());
        var configure = new MenuItem { Header = "Все темы и настройка…" };
        configure.Click += (_, _) => ShowThemes(this); ThemesMenu.Items.Add(configure);
    }
    private void Themes_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement anchor)
        { var menu = NewMenu(); RefreshThemeMenu(); foreach (var item in ThemesMenu.Items.Cast<object>().ToList()) { ThemesMenu.Items.Remove(item); menu.Items.Add(item); } OpenMenu(anchor, menu); }
    }
    private void ShowThemes(Window owner) => CreateThemesDialog(owner).ShowDialog();
    private FormDialog CreateThemesDialog(Window owner)
    {
        var preview = catalog.Copy().Settings;
        var form = new FormDialog("Темы и избранные стили", 790, 650) { Owner = owner };
        var favorites = new List<ComboBox>();
        form.Section("Оформление");
        form.Note("Выбор сразу виден в окнах. «Сохранить» запомнит изменения, «Отмена» вернёт прежнюю тему.");
        var choice = form.Choice("theme", "Текущая тема", ThemeService.Selected(preview).Name, ThemeService.All(preview).Select(t => t.Name));
        var intensity = form.Text("intensity", "Выделение: 0–255", preview.SelectionIntensity.ToString());
        void Preview()
        {
            if (choice.SelectedItem is not string name) return;
            preview.Theme = name;
            if (int.TryParse(intensity.Text, out int value) && value is >= 0 and <= 255) preview.SelectionIntensity = value;
            ThemeService.Apply(preview); BuildRows();
        }
        choice.SelectionChanged += (_, _) => Preview(); intensity.TextChanged += (_, _) => Preview();
        void RefreshChoices(string selected)
        {
            var names = ThemeService.All(preview).Select(t => t.Name).ToArray();
            choice.ItemsSource = names; choice.SelectedItem = selected;
            foreach (var box in favorites) { string? old = box.SelectedItem as string; box.ItemsSource = names.Prepend("—").ToArray(); box.SelectedItem = names.Contains(old) ? old : "—"; }
        }
        form.ActionButton("Импортировать темы .vsf или .json…", () =>
        {
            var dialog = new OpenFileDialog { Title = "Импорт тем", Filter = "Темы|*.vsf;*.json", Multiselect = true };
            if (dialog.ShowDialog(form) != true) return;
            var imported = dialog.FileNames.Select(ThemeService.Read).ToList();
            foreach (var theme in imported) ThemeService.Upsert(preview, theme);
            RefreshChoices(imported[0].Name);
            form.SetError("Импортировано: " + imported.Count + ". Нажмите «Сохранить».");
        });
        form.ActionButton("Загрузить палитры из папки Styles…", () =>
        {
            var dialog = new OpenFolderDialog { Title = "Папка Styles из StartManager" };
            if (dialog.ShowDialog(form) != true) return;
            int count = ThemeService.ImportFolder(preview, dialog.FolderName);
            RefreshChoices(preview.Theme); form.SetError("Импортировано палитр: " + count + ". Нажмите «Сохранить».");
        });
        form.ActionButton("Создать свою тему на основе выбранной…", () =>
        {
            var theme = ThemeService.Selected(preview).Copy();
            var edit = new FormDialog("Цвета темы", 740, 650) { Owner = form };
            edit.Section("Палитра"); edit.Text("name", "Название", theme.Name + " — моя");
            string[] labels = { "Фон окна", "Фон списка и полей", "Текст", "Вторичный текст", "Границы", "Кнопки", "Наведение", "Акцент", "Выделение", "Текст выделения", "Группы", "Ошибки" };
            for (int i = 0; i < ThemeService.ColorKeys.Length; i++) edit.ColorField(ThemeService.ColorKeys[i], labels[i], theme.Colors[ThemeService.ColorKeys[i]]);
            edit.Validate = () =>
            {
                theme.Name = edit.Value("name"); theme.Source = ""; theme.SourceHash = "";
                foreach (string key in ThemeService.ColorKeys) theme.Colors[key] = edit.Value(key).ToUpperInvariant();
                ThemeService.Validate(theme); return true;
            };
            if (edit.ShowDialog() != true) return;
            ThemeService.Upsert(preview, theme); RefreshChoices(theme.Name);
        });
        form.ActionButton("Экспортировать выбранную палитру…", () =>
        {
            var dialog = new SaveFileDialog { Title = "Экспорт палитры ВанБаз", FileName = "VanBaz-theme.json", Filter = "Палитра ВанБаз|*.json" };
            if (dialog.ShowDialog(form) == true) File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(ThemeService.Selected(preview), new JsonSerializerOptions { WriteIndented = true }));
        });
        form.Note(".vsf: переносятся цвета VCL_STYLE 2.0. Кнопки и прочие элементы рисует ВанБаз; изображения и рамка окна Windows из оригинала не копируются.");
        form.Heading("Образец внешнего вида");
        var sample = new System.Windows.Controls.ListBox { Height = 100, Margin = new(0, 0, 0, 8) };
        sample.Items.Add("▸ Демонстрационная группа"); sample.Items.Add("Тестовая база · 8.3"); sample.Items.Add("Другая тестовая база · 8.4"); sample.SelectedIndex = 1; form.Element(sample);
        form.Section("Пять избранных");
        form.Note("Эти темы появятся в сервисном меню «Темы». Пустое место можно оставить знаком «—».");
        for (int i = 0; i < 5; i++) favorites.Add(form.Choice("favorite" + i, "Стиль " + (i + 1), preview.FavoriteThemes.ElementAtOrDefault(i) ?? "—", ThemeService.All(preview).Select(t => t.Name).Prepend("—")));
        form.Validate = () =>
        {
            if (!int.TryParse(form.Value("intensity"), out int value) || value is < 0 or > 255) return form.SetError("Интенсивность выделения: целое число от 0 до 255.");
            preview.Theme = form.Value("theme"); preview.SelectionIntensity = value;
            preview.FavoriteThemes = favorites.Select(b => b.SelectedItem as string ?? "—").Where(n => n != "—").Distinct().Take(5).ToList();
            return true;
        };
        form.ApplyChanges = () =>
        {
            bool saved = Commit(() =>
                {
                    catalog.Settings.Theme = preview.Theme; catalog.Settings.Themes = preview.Themes.Select(t => t.Copy()).ToList();
                    catalog.Settings.FavoriteThemes = preview.FavoriteThemes.ToList(); catalog.Settings.SelectionIntensity = preview.SelectionIntensity;
                });
            if (saved) ApplyPreferences();
            return saved;
        };
        form.Closed += (_, _) => ApplyPreferences();
        return form;
    }
}
