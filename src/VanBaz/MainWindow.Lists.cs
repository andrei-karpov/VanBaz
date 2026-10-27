using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public partial class MainWindow
{
    private void AlternativeLists_Click(object sender, RoutedEventArgs e) => EditListSources();
    private void RefreshLists_Click(object sender, RoutedEventArgs e) => ReadListSources();
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || Selected is not { } b) return;
        var configurations = catalog.Configurations.Select(c => (Label: c.Name + " [" + c.Id + "]", Source: c)).ToList();
        var selectedSource = configurations.FirstOrDefault(c => c.Source.Id == b.ConfigurationId);
        string url = selectedSource.Source?.InfoUrl ?? b.UpdateInfoUrl;
        if (url.Length == 0)
        {
        var f = new FormDialog("Проверка релиза · " + b.Name, 770, 490) { Owner = this };
        f.Section("Конфигурация"); f.Choice("source", "Из каталога", selectedSource.Label ?? "", new[] { "" }.Concat(configurations.Select(c => c.Label)));
        f.Section("Источник релизов"); f.Text("version", "Текущая версия", b.ConfigurationVersion); f.Text("url", "UpdInfo.txt (HTTPS)", b.UpdateInfoUrl);
        f.Note("Укажите адрес публичного UpdInfo.txt производителя именно этой конфигурации. Проверяется номер релиза из файла; данные базы и пароли не отправляются. Доступность и актуальность публикации зависят от производителя.");
        if (b.ReleaseChecked.HasValue) f.Note($"Последняя проверка: {b.ReleaseChecked:dd.MM.yyyy HH:mm}. В источнике: {b.LatestRelease}.");
        f.SetAcceptText("Проверить");
        f.Validate = () => configurations.Any(c => c.Label == f.Value("source")) || Uri.TryCreate(f.Value("url"), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 || f.SetError("Выберите конфигурацию из каталога или укажите HTTPS-адрес.");
        if (f.ShowDialog() != true) return;
        var choice = configurations.FirstOrDefault(c => c.Label == f.Value("source"));
        if (!Commit(() => { b.UpdateInfoUrl = f.Value("url"); b.ConfigurationVersion = f.Value("version"); b.ConfigurationId = choice.Source?.Id ?? ""; })) return;
        url = choice.Source?.InfoUrl ?? b.UpdateInfoUrl;
        }
        if (isolatedSession) return;
        try
        {
            StatusText.Text = "Проверяется опубликованный релиз…";
            var info = await ReleaseService.Fetch(url, catalog.Settings);
            string result = System.Version.TryParse(b.ConfigurationVersion, out var current) ? new Version(info.Version).CompareTo(current) switch { > 0 => "В источнике опубликован более новый релиз.", 0 => "Номера версий совпадают.", _ => "Версия в источнике старше указанной версии базы." } : "Текущая версия базы не задана: сравнение не выполнено.";
            Commit(() => { b.LatestRelease = info.Version; b.ReleaseChecked = DateTime.Now; });
            var output = new FormDialog("Результат проверки релиза", 780, 540) { Owner = this }; output.Section("Релиз");
            output.Note(result + "\n\nРелиз: " + info.Version + "\nТекущий: " + b.ConfigurationVersion + "\nДата публикации: " + info.Date + "\nОбновление с версий: " + info.FromVersions + "\nИсточник: " + url);
            var source = catalog.Configurations.FirstOrDefault(c => c.Id == b.ConfigurationId);
            if (source != null) output.ActionButton("Дистрибутивы…", () => OpenDistributions(source, output));
            output.Note("Для установки используйте «Обновления → Установить CFU / CF».");
            output.SetAcceptText("Закрыть"); output.ShowDialog();
        }
        catch (Exception ex) { StatusText.Text = "Проверка релиза не выполнена."; Error("Проверка релиза", ex); }
    }
    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (locked) return;
        var setup = new Window { Owner = this, Title = "Временная блокировка ВанБаз", Width = 430, Height = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new(16) }; setup.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Задайте пароль на время этой блокировки.\nОн не сохраняется после разблокировки.\nЭто не замена блокировке Windows (Win+L).", Margin = new(0, 0, 0, 12) });
        var password = new PasswordBox { MinHeight = 28 }; panel.Children.Add(password);
        var button = new Button { Content = "Заблокировать", Margin = new(0, 12, 0, 0), IsDefault = true }; panel.Children.Add(button);
        button.Click += (_, _) => { if (password.Password.Length > 0) setup.DialogResult = true; };
        if (setup.ShowDialog() != true) return;
        byte[] salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        byte[] hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password.Password, salt, 100000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32); password.Clear();
        locked = true; RefreshTrayMenu();
        var unlock = new ApplicationLockWindow { Owner = this };
        var hint = unlock.HintText; var key = unlock.Password; var ok = unlock.Accept;
        unlock.Closing += (_, ev) => ev.Cancel = locked;
        ok.Click += (_, _) =>
        {
            var candidate = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(key.Password, salt, 100000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            bool matches = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hash, candidate); Array.Clear(candidate); key.Clear();
            if (matches) { locked = false; unlock.Close(); } else hint.Text = "Неверный пароль.";
        };
        unlock.ShowDialog(); Array.Clear(hash); Array.Clear(salt); RefreshTrayMenu();
    }
}
