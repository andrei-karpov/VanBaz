using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Win32;

namespace VanBaz;

public static class ServerConsole
{
    public static ProcessStartInfo CreatePlan(PlatformInfo platform, string windowsFolder, bool os64, bool process64, Func<string, string, string?>? registeredDll = null)
    {
        if (platform.Architecture is not ("x86" or "x64") || platform.Architecture == "x64" && !os64) throw new InvalidOperationException("Архитектура консоли не поддерживается этой системой.");
        string bin = Path.GetFullPath(platform.Directory), dll = Path.Combine(bin, "radmin.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Для этой платформы не установлена оснастка администрирования radmin.dll.", dll);
        string common = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(bin))!, "common");
        string name = platform.Architecture == "x64" ? "1CV8 Servers (x86-64).msc" : "1CV8 Servers.msc";
        string msc = new[] { Path.Combine(bin, name), Path.Combine(common, name) }.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Не найден штатный файл консоли " + name + ". Установите компоненту администрирования серверов 1С.");
        registeredDll ??= RegisteredDll;
        var classes = XDocument.Load(msc).Descendants("Snapin").Select(n => (string?)n.Attribute("CLSID")).Where(s => Guid.TryParse(s, out _));
        bool matching = classes.Any(id => string.Equals(registeredDll(id!, platform.Architecture)?.Trim().Trim('"'), dll, StringComparison.OrdinalIgnoreCase));
        if (!matching) throw new InvalidOperationException("Оснастка выбранной версии не зарегистрирована. Используйте вкладку «Компоненты и службы» или установщик 1С.");
        string system = platform.Architecture == "x86" && os64 ? "SysWOW64" : platform.Architecture == "x64" && !process64 ? "Sysnative" : "System32";
        string mmc = Path.Combine(windowsFolder, system, "mmc.exe");
        if (!File.Exists(mmc)) throw new FileNotFoundException("Не найден Microsoft Management Console.", mmc);
        var info = new ProcessStartInfo(mmc) { UseShellExecute = false, WorkingDirectory = bin };
        info.ArgumentList.Add(msc); if (os64) info.ArgumentList.Add(platform.Architecture == "x64" ? "/64" : "/32"); return info;
    }
    private static string? RegisteredDll(string clsid, string architecture)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, architecture == "x64" ? RegistryView.Registry64 : RegistryView.Registry32);
        using var key = root.OpenSubKey("CLSID\\" + clsid + "\\InprocServer32"); return key?.GetValue(null) as string;
    }
}

public partial class MainWindow
{
    private Window CreateServerConsoleWindow(Func<PlatformInfo, ProcessStartInfo>? planner = null, Action<ProcessStartInfo>? launch = null)
    {
        planner ??= p => ServerConsole.CreatePlan(p, Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.Is64BitOperatingSystem, Environment.Is64BitProcess);
        launch ??= info => { if (!isolatedSession) Process.Start(info); };
        var window = new Window { Title = "Консоль администрирования серверов 1С", Width = 890, Height = 640, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }; DialogBehavior.FitToScreen(window);
        var tabs = new TabControl(); window.Content = tabs;
        var root = new StackPanel { Margin = new Thickness(14) }; tabs.Items.Add(new TabItem { Header = "Консоль", Content = root });
        root.Children.Add(new TextBlock { Text = "Установленная платформа", Margin = new Thickness(0, 0, 0, 7) });
        var version = new ComboBox { Name = "ServerConsolePlatform", ItemsSource = platforms, DisplayMemberPath = "Display", SelectedIndex = platforms.Count > 0 ? 0 : -1 }; root.Children.Add(version);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) }; root.Children.Add(status);
        root.Children.Add(new TextBlock { Text = "Откроется штатная MMC-консоль. Для регистрации компонентов и управления службами используйте следующую вкладку: команды показываются перед выполнением и запрашивают права администратора.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; root.Children.Add(buttons);
        var open = new Button { Name = "OpenServerConsole", Content = "Открыть консоль", IsDefault = true }; buttons.Children.Add(open);
        var close = new Button { Content = "Закрыть", IsCancel = true }; close.Click += (_, _) => window.Close(); buttons.Children.Add(close);
        void Refresh()
        {
            open.IsEnabled = false;
            if (version.SelectedItem is not PlatformInfo p) { status.Text = "Платформы не найдены. Обновите список установленных платформ."; return; }
            try { var plan = planner(p); status.Text = p.Directory + "\n" + plan.ArgumentList[0]; open.IsEnabled = CanEdit; }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        version.SelectionChanged += (_, _) => Refresh(); Refresh();
        open.Click += (_, _) => { if (!CanEdit || version.SelectedItem is not PlatformInfo p) return; try { launch(planner(p)); window.Close(); } catch (Exception ex) { status.Text = ex.Message; open.IsEnabled = false; } };
        tabs.Items.Add(new TabItem { Header = "Компоненты и службы", Content = CreateServerAdministrationPanel(window, () => Refresh()) });
        return window;
    }
    private void ServerConsole_Click(object sender, RoutedEventArgs e) { if (AllowTools()) CreateServerConsoleWindow().ShowDialog(); }
}
