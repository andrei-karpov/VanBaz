using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VanBaz;

public partial class MainWindow
{
    private FrameworkElement CreateServerAdministrationPanel(Window owner, Action refreshed, Func<IReadOnlyList<ServerService>>? read = null, Func<ServerAdminCommand, Task>? execute = null, Action<PlatformInfo>? openConsole = null, Func<string, bool>? confirm = null)
    {
        read ??= isolatedSession ? () => Array.Empty<ServerService>() : () => ServerServices.Read(false);
        execute ??= command => isolatedSession ? Task.CompletedTask : ServerAdministration.Execute(command);
        openConsole ??= p => { if (!isolatedSession) Process.Start(ServerConsole.CreatePlan(p, Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.Is64BitOperatingSystem, Environment.Is64BitProcess)); };
        confirm ??= preview => MessageBox.Show(owner, preview + "\n\nВыполнить? Уже завершённые внешние команды автоматически не отменяются.", "План команд", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows); bool os64 = Environment.Is64BitOperatingSystem, process64 = Environment.Is64BitProcess;
        var root = new DockPanel { Margin = new Thickness(12) }; var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var version = new ComboBox { Name = "AdministrationPlatform", ItemsSource = platforms, DisplayMemberPath = "Display", SelectedIndex = platforms.Count > 0 ? 0 : -1 }; top.Children.Add(version);
        var checks = new WrapPanel(); top.Children.Add(checks);
        var stop = new CheckBox { Content = "Остановить выбранные службы" }; var start = new CheckBox { Content = "Запустить выбранные службы" }; var register = new CheckBox { Content = "Зарегистрировать radmin.dll" }; var mmc = new CheckBox { Content = "Открыть MMC", IsChecked = true };
        foreach (var check in new[] { stop, start, register, mmc }) checks.Children.Add(check);
        var more = new WrapPanel(); top.Children.Add(more); var silent = new CheckBox { Content = "Тихая регистрация", IsChecked = true }; var closeAfter = new CheckBox { Content = "Закрыть после выполнения" }; more.Children.Add(silent); more.Children.Add(closeAfter);
        var every = new Button { Content = "Отметить всё" }; var none = new Button { Content = "Снять отметки" }; more.Children.Add(every); more.Children.Add(none);
        every.Click += (_, _) => { foreach (var check in new[] { stop, start, register, mmc }) check.IsChecked = true; }; none.Click += (_, _) => { foreach (var check in new[] { stop, start, register, mmc }) check.IsChecked = false; };
        top.Children.Add(new TextBlock { Text = "Остановка службы прерывает работу её клиентов. Выбор платформы относится к DLL/MMC. Запуск использует существующий EXE отмеченной службы; для замены есть отдельная команда «Сменить версию службы». Регистрация компонентов выполняется для компьютера с UAC.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) });
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var status = new TextBlock { Name = "ServerAdministrationStatus", TextWrapping = TextWrapping.Wrap }; bottom.Children.Add(status); var buttons = new WrapPanel(); bottom.Children.Add(buttons);
        var grid = new DataGrid { Name = "ServerServicesGrid", SelectionMode = DataGridSelectionMode.Extended, IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false }; root.Children.Add(grid);
        foreach (var column in new[] { ("Имя", "Name", 180d), ("Описание", "Description", 190d), ("Состояние", "State", 105d), ("Вход от имени", "Account", 130d), ("EXE", "Executable", 270d), ("Bit", "Architecture", 55d) }) grid.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2), Width = column.Item3 });
        bool busy = false; var serviceButtons = new List<Button>();
        void States() { top.IsEnabled = grid.IsEnabled = !busy; foreach (Button b in buttons.Children) b.IsEnabled = !busy && CanEdit; foreach (var b in serviceButtons) b.IsEnabled = !busy && CanEdit && grid.SelectedItems.Count > 0; }
        async Task Refresh() { if (busy) return; var names = grid.SelectedItems.Cast<ServerService>().Select(s => s.Name).ToHashSet(); busy = true; States(); try { grid.ItemsSource = await Task.Run(read); foreach (ServerService item in grid.Items) if (names.Contains(item.Name)) grid.SelectedItems.Add(item); status.Text = grid.Items.Count == 0 ? "Службы агента не найдены или недоступны. Для первоначальной установки службы используйте установщик 1С." : "Выберите одну или несколько служб."; refreshed(); } catch (Exception ex) { status.Text = ex.Message; } finally { busy = false; States(); } }
        async Task Run(IEnumerable<ServerAdminCommand> source, bool console = false)
        {
            if (busy || !CanEdit) return; var commands = source.ToArray(); var selectedPlatform = version.SelectedItem as PlatformInfo;
            if (commands.Length == 0 && !console) { status.Text = "Не выбрано ни одного действия."; return; }
            string preview = string.Join("\n\n", commands.Select(c => c.Preview)) + (console ? "\n\nОткрыть MMC выбранной платформы после успешных команд." : "");
            if (!confirm(preview)) return;
            busy = true; States(); operations.Add("@server-administration"); var done = new List<string>();
            try { foreach (var command in commands) { status.Text = command.Caption; await execute(command); done.Add(command.Caption); } if (console && selectedPlatform != null) openConsole(selectedPlatform); status.Text = "Готово. " + string.Join("; ", done); if (closeAfter.IsChecked == true) { busy = false; owner.Close(); } }
            catch (Exception ex) { status.Text = "Выполнено: " + (done.Count == 0 ? "нет" : string.Join("; ", done)) + "\nОстановлено: " + ex.Message; }
            finally { operations.Remove("@server-administration"); busy = false; string result = status.Text; if (owner.IsVisible) await Refresh(); status.Text = result; States(); refreshed(); }
        }
        PlatformInfo Platform() => version.SelectedItem as PlatformInfo ?? throw new InvalidOperationException("Выберите установленную платформу.");
        Button Button(string caption, Func<Task> action, bool requiresSelection = false)
        { var button = new Button { Content = caption }; buttons.Children.Add(button); if (requiresSelection) serviceButtons.Add(button); button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { status.Text = ex.Message; } }; return button; }
        Button("Выполнить отмеченное…", () =>
        {
            var commands = new List<ServerAdminCommand>(); var selected = grid.SelectedItems.Cast<ServerService>().ToArray();
            if ((stop.IsChecked == true || start.IsChecked == true) && selected.Length == 0) throw new InvalidOperationException("Выберите службы в таблице.");
            if (stop.IsChecked == true) commands.AddRange(selected.Where(s => s.State == "Работает").Select(s => ServerAdministration.Service(s, "stop", windows, os64, process64)));
            if (register.IsChecked == true) commands.Add(ServerAdministration.Registration(Platform(), "radmin.dll", false, silent.IsChecked == true, windows, os64, process64));
            if (start.IsChecked == true) commands.AddRange(selected.Where(s => stop.IsChecked == true || s.State != "Работает").Select(s => ServerAdministration.Service(s, "start", windows, os64, process64)));
            if (mmc.IsChecked == true) Platform(); return Run(commands, mmc.IsChecked == true);
        });
        Button("COMConnector…", () => Run(new[] { ServerAdministration.Registration(Platform(), "comcntr.dll", false, silent.IsChecked == true, windows, os64, process64) }));
        Button("Снять регистрацию…", async () =>
        {
            var form = new FormDialog("Снятие регистрации компоненты", 560, 330) { Owner = owner }; form.Section("Компонента"); form.Choice("dll", "DLL", "radmin.dll", new[] { "radmin.dll", "comcntr.dll" });
            if (form.ShowDialog() == true) await Run(new[] { ServerAdministration.Registration(Platform(), form.Value("dll"), true, silent.IsChecked == true, windows, os64, process64) });
        });
        Button("Сменить версию службы…", () => Run(grid.SelectedItems.Cast<ServerService>().Select(s => ServerAdministration.Service(s, "config", windows, os64, process64, Platform())).ToArray()), true);
        Button("Обновить", Refresh);
        Button("Службы Windows", () => { if (!isolatedSession) Process.Start(new ProcessStartInfo(Path.Combine(windows, "System32", "mmc.exe")) { UseShellExecute = true, Arguments = DesktopIntegration.Quote(Path.Combine(windows, "System32", "services.msc")) }); return Task.CompletedTask; });
        grid.SelectionChanged += (_, _) => States(); owner.Closing += (_, e) => { if (busy) { e.Cancel = true; status.Text = "Дождитесь завершения внешней команды."; } };
        root.Loaded += async (_, _) => { if (grid.ItemsSource == null) await Refresh(); }; States(); return root;
    }
}
