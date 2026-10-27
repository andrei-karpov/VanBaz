using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VanBaz;

// Shared by base properties and the context menu. No database is contacted until Read is clicked.
public sealed class SessionLockPanel : StackPanel
{
    public static IReadOnlyList<int> BeginPresets { get; } = new[] { 0, 5, 10, 15, 20, 25, 30, 45, 50, 60, 120, 180 };
    public static IReadOnlyList<int> EndPresets { get; } = new[] { 5, 10, 15, 20, 25, 30, 60, 120, 180 };
    public static string PresetCaption(int minutes, bool end) => minutes == 0 ? "Немедленно" : (end ? "На " : "Через ") + (minutes < 60 ? minutes + " минут" : minutes == 60 ? "1 час" : minutes / 60 + " часа");
    public const string EmptyDate = "0001-01-01T00:00:00";
    private readonly BaseEntry entry;
    private readonly bool allowed;
    private readonly Func<string, string, string, SessionLockState?, SessionLockState?, Task<BridgeResult>> execute;
    private readonly ComboBox architecture = new() { ItemsSource = new[] { "x86", "x64" }, Width = 70 };
    private readonly PasswordBox currentCode = new() { MinWidth = 80 };
    private readonly Button read = new() { Content = "Прочитать", MinWidth = 85 };
    private readonly Button apply = new() { Content = UiIcons.Label("lock", "Изменить"), MinWidth = 95, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox enabled = new() { Content = "Заблокировать информационную базу", Margin = new(0, 8, 0, 8) };
    private readonly CheckBox useBegin = new() { Content = "Начало:", Width = 94 };
    private readonly CheckBox useEnd = new() { Content = "Окончание:", Width = 94 };
    private readonly DatePicker beginDate = new() { Width = 112, SelectedDate = DateTime.Today };
    private readonly DatePicker endDate = new() { Width = 112, SelectedDate = DateTime.Today };
    private readonly TextBox beginTime = new() { Width = 64, Text = DateTime.Now.ToString("HH:mm:ss") };
    private readonly TextBox endTime = new() { Width = 64, Text = DateTime.Now.AddHours(1).ToString("HH:mm:ss") };
    private readonly TextBox message = new() { Height = 68, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox key = new() { MinWidth = 140 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 4) };
    private readonly StackPanel values = new();
    private readonly StackPanel details = new() { Margin = new(18, 0, 0, 0) };
    private SessionLockState? observed;
    private bool busy;
    private string readArchitecture = "", readCode = "";
    public SessionLockPanel(BaseEntry entry, bool allowOperations,
        Func<string, string, string, SessionLockState?, SessionLockState?, Task<BridgeResult>>? bridge = null)
    {
        this.entry = entry;
        allowed = allowOperations && entry.Kind == "File" && !entry.Version.StartsWith("7.") && !entry.Version.StartsWith("8.1");
        execute = bridge ?? ((operation, arch, code, value, expected) => PlatformBridge.Execute(entry, operation, arch, code, value, expected));
        architecture.SelectedItem = entry.Architecture == "x86" ? "x86" : "x64";
        foreach (var date in new[] { beginDate, endDate })
        {
            date.Height = 24; date.Padding = new(0); date.FontSize = 11;
            date.SetResourceReference(Control.BackgroundProperty, "ThemeSurface");
            date.SetResourceReference(Control.ForegroundProperty, "ThemeText");
            date.SetResourceReference(Control.BorderBrushProperty, "ThemeBorder");
            var textStyle = new Style(typeof(System.Windows.Controls.Primitives.DatePickerTextBox));
            textStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ThemeSurface")));
            textStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ThemeText")));
            textStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
            date.Resources[typeof(System.Windows.Controls.Primitives.DatePickerTextBox)] = textStyle;
            var buttonStyle = new Style(typeof(Button), (Style)Application.Current.FindResource(typeof(Button)));
            buttonStyle.Setters.Add(new Setter(Control.MinHeightProperty, 18d)); buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            buttonStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            date.Resources[typeof(Button)] = buttonStyle;
        }
        beginTime.Height = endTime.Height = 24;
        var connection = new WrapPanel { Margin = new(0, 0, 0, 4) };
        connection.Children.Add(Label("COM:")); connection.Children.Add(architecture);
        connection.Children.Add(Label("Код /UC:")); connection.Children.Add(currentCode); connection.Children.Add(read); Children.Add(connection);
        Children.Add(values); values.Children.Add(enabled); values.Children.Add(details);
        details.Children.Add(DateRow(useBegin, beginDate, beginTime, false)); details.Children.Add(DateRow(useEnd, endDate, endTime, true));
        details.Children.Add(Label("Текст сообщения пользователям:"));
        var messageRow = new DockPanel(); var template = new Button { Content = "{..}", ToolTip = "Шаблон текста", Width = 27, VerticalAlignment = VerticalAlignment.Top, Padding = new(0) };
        DockPanel.SetDock(template, Dock.Right); messageRow.Children.Add(template); messageRow.Children.Add(message); details.Children.Add(messageRow);
        template.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (string text in new[] { "Проводятся технические работы...", "Выполняется обновление информационной базы..." })
            { var item = new MenuItem { Header = text, Icon = UiIcons.Icon("▣") }; item.Click += (_, _) => message.Text = text; menu.Items.Add(item); }
            template.ContextMenu = menu; menu.PlacementTarget = template; menu.IsOpen = true;
        };
        var keyRow = new DockPanel { Margin = new(0, 7, 0, 5) }; var keyLabel = Label("Код разрешения:"); keyLabel.Width = 120;
        keyRow.Children.Add(keyLabel); keyRow.Children.Add(key); details.Children.Add(keyRow);
        key.ToolTip = "Код разрешения для входа во время блокировки. Можно оставить пустым.";
        values.Children.Add(apply); Children.Add(status);
        Children.Add(new TextBlock { Text = "Изменение применяется сразу к сохранённому подключению:\n" + entry.Location, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0) });
        status.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMuted");
        status.Text = allowed ? "Прочитайте состояние. Нужны права администратора базы и установленный COMConnector. Работающие сеансы не отключаются." : "Изменение блокировки доступно для сохранённой файловой базы 1С 8.2 и новее.";
        read.Click += async (_, _) => await ReadAsync(); apply.Click += async (_, _) => await ApplyAsync();
        void ConnectionChanged()
        {
            if (busy || observed == null) return;
            observed = null; status.Text = "Параметры подключения изменены. Прочитайте состояние повторно."; Refresh();
        }
        architecture.SelectionChanged += (_, _) => ConnectionChanged(); currentCode.PasswordChanged += (_, _) => ConnectionChanged();
        foreach (var check in new[] { enabled, useBegin, useEnd }) { check.Checked += (_, _) => Refresh(); check.Unchecked += (_, _) => Refresh(); }
        Loaded += (_, _) => { if (Window.GetWindow(this) is { } window) window.Closing += (_, e) => { if (busy) { e.Cancel = true; status.Text = "Дождитесь завершения операции с блокировкой."; } }; };
        Refresh();
    }
    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 3, 6, 3) };
    private UIElement DateRow(CheckBox check, DatePicker date, TextBox time, bool end)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 6) };
        row.Children.Add(check); row.Children.Add(date); time.Margin = new(5, 0, 0, 0); row.Children.Add(time);
        var presets = new Button { Content = "…", ToolTip = end ? "Продолжительность от начала" : "Начало от текущего времени", Width = 24, Padding = new(0) }; row.Children.Add(presets);
        presets.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (int minutes in end ? EndPresets : BeginPresets)
            {
                int offset = minutes;
                var item = new MenuItem { Header = PresetCaption(minutes, end), Icon = UiIcons.Icon("history") };
                item.Click += (_, _) =>
                {
                    DateTime origin = DateTime.Now;
                    if (end && useBegin.IsChecked == true && TryDate(beginDate, beginTime, out var begin)) origin = begin;
                    var value = origin.AddMinutes(offset); check.IsChecked = true; date.SelectedDate = value.Date; time.Text = value.ToString("HH:mm:ss");
                }; menu.Items.Add(item);
            }
            presets.ContextMenu = menu; menu.PlacementTarget = presets; menu.IsOpen = true;
        };
        return row;
    }
    private void Refresh()
    {
        read.IsEnabled = architecture.IsEnabled = currentCode.IsEnabled = allowed && !busy;
        values.IsEnabled = allowed && !busy && observed != null;
        details.IsEnabled = enabled.IsChecked == true;
        beginDate.IsEnabled = beginTime.IsEnabled = useBegin.IsChecked == true;
        endDate.IsEnabled = endTime.IsEnabled = useEnd.IsChecked == true;
    }
    private static bool TryDate(DatePicker date, TextBox time, out DateTime result)
    {
        result = default;
        if (date.SelectedDate is not { } day || !DateTime.TryParseExact(time.Text.Trim(), new[] { "HH:mm:ss", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock)) return false;
        result = day.Date.Add(clock.TimeOfDay); return true;
    }
    private void LoadState(SessionLockState state)
    {
        observed = JsonSerializer.Deserialize<SessionLockState>(JsonSerializer.Serialize(state))!;
        enabled.IsChecked = state.Enabled; message.Text = state.Message; key.Text = state.KeyCode;
        void Load(string value, CheckBox check, DatePicker date, TextBox time)
        {
            bool valid = DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) && parsed.Year > 1;
            check.IsChecked = valid; if (valid) { date.SelectedDate = parsed.Date; time.Text = parsed.ToString("HH:mm:ss"); }
        }
        Load(state.Begin, useBegin, beginDate, beginTime); Load(state.End, useEnd, endDate, endTime); Refresh();
    }
    public SessionLockState ReadValue()
    {
        if (observed == null) throw new InvalidOperationException("Сначала прочитайте состояние блокировки.");
        string Date(CheckBox use, DatePicker date, TextBox time)
        {
            if (use.IsChecked != true) return EmptyDate;
            if (!TryDate(date, time, out var value)) throw new InvalidOperationException("Проверьте дату и время (чч:мм:сс).");
            return value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }
        var value = new SessionLockState { Enabled = enabled.IsChecked == true, Begin = Date(useBegin, beginDate, beginTime), End = Date(useEnd, endDate, endTime), Message = message.Text, KeyCode = key.Text, Parameter = observed.Parameter };
        if (value.Enabled && value.End != EmptyDate && (string.CompareOrdinal(value.End, value.Begin) <= 0 || DateTime.ParseExact(value.End, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) <= DateTime.Now))
            throw new InvalidOperationException("Окончание должно быть позже начала и текущего времени.");
        return value;
    }
    public async Task ReadAsync()
    {
        if (!allowed || busy) return;
        busy = true; observed = null; Refresh(); status.Text = "Чтение состояния…";
        try
        {
            readArchitecture = (string)architecture.SelectedItem; readCode = currentCode.Password;
            var result = await execute("get-lock", readArchitecture, readCode, null, null);
            LoadState(result.Lock ?? throw new InvalidOperationException("Платформа не вернула состояние."));
            status.Text = "Состояние прочитано. «Изменить» сразу применяет параметры к базе.";
        }
        catch (Exception ex) { observed = null; status.Text = ex.Message; }
        finally { busy = false; Refresh(); }
    }
    public async Task ApplyAsync()
    {
        if (!allowed || busy || observed == null) return;
        SessionLockState next;
        try { next = ReadValue(); } catch (Exception ex) { status.Text = ex.Message; return; }
        busy = true; Refresh(); status.Text = "Изменение и проверка блокировки…";
        try
        {
            var result = await execute("set-lock", readArchitecture, readCode, next, observed);
            if (JsonSerializer.Serialize(result.Lock) != JsonSerializer.Serialize(next)) throw new InvalidOperationException("Состояние не совпало. Прочитайте блокировку повторно.");
            LoadState(result.Lock!); currentCode.Password = readCode = next.Enabled ? next.KeyCode : "";
            status.Text = next.Enabled ? "Блокировка установлена и прочитана повторно." : "Блокировка снята и прочитана повторно.";
        }
        catch (Exception ex) { observed = null; status.Text = ex.Message + " Прочитайте состояние перед новой попыткой."; }
        finally { busy = false; Refresh(); }
    }
}
