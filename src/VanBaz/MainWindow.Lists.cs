using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public partial class MainWindow
{
    private void AlternativeLists_Click(object sender, RoutedEventArgs e) => EditListSources();
    private void RefreshLists_Click(object sender, RoutedEventArgs e) => ReadListSources();
    private void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || Selected is not { } entry) return;
        CreateReleaseCheckWindow(entry).ShowDialog();
    }
    private Window CreateReleaseCheckWindow(BaseEntry entry,
        Func<string, bool, System.Threading.CancellationToken, Task<ReleaseInfo>>? read = null)
    {
        return new BaseReleaseWindow(entry.Copy(), catalog.Configurations,
            read ?? ((address, local, token) => local ? ReleaseService.ReadLocal(address, token)
                : isolatedSession ? Task.FromException<ReleaseInfo>(new InvalidOperationException("В проверочном сеансе сеть отключена."))
                : ReleaseService.Fetch(address, catalog.Settings, token)),
            selection => CanEdit && Commit(() =>
            {
                var current = catalog.Bases.Single(b => b.Id == entry.Id);
                current.ConfigurationVersion = selection.CurrentVersion;
                current.UseLocalUpdateInfo = selection.Local;
                if (selection.Local) current.LocalUpdateInfoFile = selection.Address;
                else { current.UpdateInfoUrl = selection.Address; current.ConfigurationId = selection.ConfigurationId; }
                current.LatestRelease = selection.Release.Version; current.ReleaseChecked = DateTime.Now;
            }),
            (owner, source, version) => { if (!isolatedSession && CanEdit) OpenDistributions(source, owner, version); }) { Owner = this };
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
