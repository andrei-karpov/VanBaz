using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VanBaz;

public class FormDialog : Window
{
    private readonly Dictionary<string, Control> fields = new();
    private readonly HashSet<string> changedPasswords = new();
    private readonly Dictionary<string, StackPanel> panels = new();
    private readonly ListBox navigation = new() { Width = 180, Margin = new(0, 0, 10, 0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 5) };
    private StackPanel current = null!;
    private readonly Button accept;
    private readonly Button cancel;
    private TreeView? sectionTree;
    private readonly Dictionary<string, TreeViewItem> treeSections = new();
    public Func<bool>? Validate { get; set; }
    public Func<bool>? ApplyChanges { get; set; }
    public FormDialog(string title, double width = 760, double height = 570)
    {
        Title = title; Width = Math.Min(width, SystemParameters.WorkArea.Width); Height = Math.Min(height, SystemParameters.WorkArea.Height); MinWidth = Math.Min(474, width); MinHeight = Math.Min(300, height);
        Style = (Style)Application.Current.FindResource(typeof(Window)); UseLayoutRounding = true;
        error.SetResourceReference(TextBlock.ForegroundProperty, "ThemeError");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display); TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DialogBehavior.FitToScreen(this);
        var itemTemplate = new DataTemplate();
        var caption = new FrameworkElementFactory(typeof(TextBlock));
        caption.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        caption.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        itemTemplate.VisualTree = caption; navigation.ItemTemplate = itemTemplate;
        ScrollViewer.SetHorizontalScrollBarVisibility(navigation, ScrollBarVisibility.Disabled);
        SizeChanged += (_, _) => navigation.Width = Math.Clamp(ActualWidth * .25, 130, 200);
        var root = new DockPanel { Margin = new Thickness(10) }; Content = root;
        var footer = new DockPanel { LastChildFill = true, Margin = new(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var buttons = new WrapPanel { MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons); footer.Children.Add(error);
        cancel = new Button { Content = "Отмена", Width = 100, IsCancel = true };
        var save = accept = new Button { Content = "Сохранить", MinWidth = 100, IsDefault = true };
        cancel.Click += (_, _) => DialogResult = false;
        save.Click += (_, _) => { try { if (Validate?.Invoke() != false && ApplyChanges?.Invoke() != false) DialogResult = true; } catch (Exception ex) { SetError(ex.Message); } };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        var apply = new Button { Content = "Применить", MinWidth = 95, Visibility = Visibility.Collapsed };
        buttons.Children.Insert(0, apply);
        apply.Click += (_, _) => { try { if (Validate?.Invoke() != false && ApplyChanges?.Invoke() != false) error.Text = "Изменения сохранены."; } catch (Exception ex) { SetError(ex.Message); } };
        Loaded += (_, _) => { apply.Visibility = ApplyChanges == null ? Visibility.Collapsed : Visibility.Visible; if (panels.Count == 1) navigation.Visibility = Visibility.Collapsed; };
        DockPanel.SetDock(navigation, Dock.Left); root.Children.Add(navigation); root.Children.Add(scroll);
        Loaded += (_, _) => { var first = panels.Values.FirstOrDefault()?.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Control>()).FirstOrDefault(c => c.IsEnabled && c.Focusable); first?.Focus(); };
        navigation.SelectionChanged += (_, _) => { if (navigation.SelectedItem is string section) { scroll.Content = panels[section]; scroll.ScrollToTop(); error.Text = ""; } };
    }
    public void Section(string name)
    {
        current = new StackPanel { Margin = new(4, 0, 8, 0) }; panels[name] = current; navigation.Items.Add(name);
        Heading(System.Text.RegularExpressions.Regex.Replace(name, @"^\d+\.\s+", ""));
        if (navigation.SelectedIndex < 0) navigation.SelectedIndex = 0;
    }
    public void TopText(string key, string label, string value)
    {
        var row = new DockPanel { Margin = new(0, 0, 0, 12) };
        var caption = new TextBlock { Text = label, Width = 175, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(caption, Dock.Left); row.Children.Add(caption);
        var box = new TextBox { Text = value }; System.Windows.Automation.AutomationProperties.SetName(box, label); row.Children.Add(box); fields[key] = box;
        DockPanel.SetDock(row, Dock.Top); ((DockPanel)Content).Children.Insert(1, row);
    }
    public void Heading(string title)
    {
        var text = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new(0, 6, 0, 8) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "ThemeAccent"); current.Children.Add(text);
    }
    public void Note(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 10) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMuted"); current.Children.Add(block);
    }
    private void Row(string label, Control input, string key)
    {
        fields[key] = input;
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        var row = new Grid { Margin = new(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(142) }); row.ColumnDefinitions.Add(new());
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 7, 0) });
        Grid.SetColumn(input, 1); row.Children.Add(input); current.Children.Add(row);
        row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.SizeChanged += (_, _) =>
        {
            bool compact = row.ActualWidth < 380; var caption = row.Children[0];
            Grid.SetColumnSpan(caption, compact ? row.ColumnDefinitions.Count : 1);
            Grid.SetRow(input, compact ? 1 : 0); Grid.SetColumn(input, compact ? 0 : 1);
            Grid.SetColumnSpan(input, compact ? Math.Max(2, row.ColumnDefinitions.Count - 1) : 1);
            foreach (var button in row.Children.OfType<Button>()) Grid.SetRow(button, compact ? 1 : 0);
        };
    }
    public TextBox Text(string key, string label, string value, bool multiline = false)
    {
        var box = new TextBox { Text = value, MinHeight = multiline ? 68 : 23, AcceptsReturn = multiline, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
        Row(label, box, key); return box;
    }
    public ComboBox Choice(string key, string label, string value, IEnumerable<string> choices, bool editable = false)
    {
        var box = new ComboBox { IsEditable = editable, ItemsSource = choices.ToList() }; box.SelectedItem = value; if (editable) box.Text = value;
        Row(label, box, key); return box;
    }
    public void Check(string key, string label, bool value)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new(0, 3, 0, 6) }; fields[key] = box; current.Children.Add(box);
    }
    public void Password(string key, string label, string protectedValue, bool missing)
    {
        var box = new PasswordBox { Padding = new Thickness(5), MinHeight = 23 };
        if (protectedValue.Length > 0)
        {
            try { box.Password = SecretStore.Reveal(protectedValue); }
            catch { Note("Сохранённый пароль недоступен этой учётной записи Windows. Введите его заново."); }
        }
        box.PasswordChanged += (_, _) => changedPasswords.Add(key); Row(label, box, key);
        if (missing) Note("Пароль из StartManager не перенесён: введите его заново или оставьте вход через окно 1С.");
    }
    public void BrowseText(string key, string label, string value, bool folder, string filter = "Все файлы|*.*")
    {
        var box = Text(key, label, value);
        var button = new Button { Content = "…", Width = 29, MinHeight = 23, Margin = new(4, 0, 0, 0), Padding = new(0), ToolTip = "Выбрать: " + label };
        System.Windows.Automation.AutomationProperties.SetName(button, "Выбрать: " + label);
        button.Click += (_, _) =>
        {
            if (folder) { var dialog = new Microsoft.Win32.OpenFolderDialog { Title = label }; if (dialog.ShowDialog(this) == true) box.Text = dialog.FolderName; }
            else { var dialog = new Microsoft.Win32.OpenFileDialog { Title = label, Filter = filter }; if (dialog.ShowDialog(this) == true) box.Text = dialog.FileName; }
        };
        var row = (Grid)box.Parent; row.ColumnDefinitions.Add(new() { Width = new GridLength(33) });
        Grid.SetColumn(button, 2); row.Children.Add(button);
    }
    public string Value(string key) => fields[key] switch { TextBox text => text.Text.Trim(), ComboBox combo => combo.IsEditable ? combo.Text.Trim() : combo.SelectedItem as string ?? "", _ => "" };
    public void SetText(string key, string value) { if (fields[key] is TextBox box) box.Text = value; }
    public bool Flag(string key) => fields[key] is CheckBox box && box.IsChecked == true;
    protected Control Field(string key) => fields[key];
    public void SetVisible(string key, bool visible)
    {
        var row = fields[key].Parent is Grid grid ? (FrameworkElement)grid : fields[key];
        row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public void SetEnabled(string key, bool enabled)
    {
        fields[key].IsEnabled = enabled;
        if (fields[key] is not CheckBox && fields[key].Parent is Grid row) foreach (var control in row.Children.OfType<Control>().Where(c => c is not CheckBox)) control.IsEnabled = enabled;
    }
    public bool PasswordChanged(string key) => changedPasswords.Contains(key);
    public string Secret(string key, string original) => changedPasswords.Contains(key) ? SecretStore.Protect(((PasswordBox)fields[key]).Password) : original;
    public bool SetError(string message) { error.Text = message; return false; }
    public void SelectSection(int index)
    {
        navigation.SelectedIndex = index;
        if (navigation.SelectedItem is string section && treeSections.TryGetValue(section, out var item)) { item.IsSelected = true; item.BringIntoView(); }
    }
    public void SetSectionVisible(string section, bool visible)
    {
        var item = navigation.ItemContainerGenerator.ContainerFromItem(section) as ListBoxItem;
        if (item != null) item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible && navigation.SelectedItem as string == section) SelectSection(0);
    }
    public void UseTreeNavigation(params (string Caption, string Section, string? Parent)[] sections)
    {
        if (sectionTree != null) throw new InvalidOperationException("Дерево разделов уже создано.");
        var tree = sectionTree = new TreeView { Name = "SettingsNavigation", Width = 200, Margin = new Thickness(0, 0, 10, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        tree.SetResourceReference(Control.ForegroundProperty, "ThemeText");
        ScrollViewer.SetHorizontalScrollBarVisibility(tree, ScrollBarVisibility.Disabled);
        var nodes = new Dictionary<string, TreeViewItem>();
        foreach (var node in sections)
        {
            if (!panels.ContainsKey(node.Section)) throw new ArgumentException("Неизвестный раздел: " + node.Section);
            var item = new TreeViewItem { Header = new TextBlock { Text = node.Caption, TextWrapping = TextWrapping.Wrap }, Tag = node.Section, IsExpanded = true, Padding = new Thickness(3, 4, 3, 4) };
            item.SetResourceReference(Control.ForegroundProperty, "ThemeText");
            nodes[node.Caption] = item; treeSections[node.Section] = item;
            if (node.Parent == null) tree.Items.Add(item); else nodes[node.Parent].Items.Add(item);
        }
        tree.SelectedItemChanged += (_, _) => { if (tree.SelectedItem is TreeViewItem { Tag: string section }) navigation.SelectedItem = section; };
        var root = (DockPanel)Content; DockPanel.SetDock(tree, Dock.Left); root.Children.Insert(root.Children.IndexOf(navigation), tree); navigation.Visibility = Visibility.Collapsed;
        SizeChanged += (_, _) => tree.Width = Math.Clamp(ActualWidth * .27, 150, 225);
        tree.Loaded += (_, _) => (tree.SelectedItem as TreeViewItem)?.Focus();
        SelectSection(navigation.SelectedIndex);
    }
    public void SetAcceptText(string text)
    {
        accept.Content = text;
        if (text == "Закрыть") { cancel.Visibility = Visibility.Collapsed; accept.IsCancel = true; }
    }
    public void WatchCheck(string key, Action update)
    { var box = (CheckBox)fields[key]; box.Checked += (_, _) => update(); box.Unchecked += (_, _) => update(); update(); }
    public void SetChecked(string key, bool value) => ((CheckBox)fields[key]).IsChecked = value;
    public void SetChoice(string key, string value) => ((ComboBox)fields[key]).SelectedItem = value;
    public void PreserveChoice(string key, string value)
    {
        var box = (ComboBox)fields[key];
        if (value.Length > 0 && !box.Items.Contains(value)) box.ItemsSource = box.Items.Cast<string>().Append(value).ToList();
        box.SelectedItem = value;
    }
    public TextBox ColorField(string key, string label, string value)
    {
        var box = Text(key, label, value);
        var button = new Button { Width = 34, MinHeight = 24, Margin = new(4, 0, 0, 0), Padding = new(4), ToolTip = "Выбрать цвет: " + label };
        System.Windows.Automation.AutomationProperties.SetName(button, "Выбрать цвет: " + label);
        var swatch = new Border { BorderBrush = Brushes.Gray, BorderThickness = new(1), Height = 14, Width = 22 }; button.Content = swatch;
        void Preview() { try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(box.Text)); } catch { swatch.Background = Brushes.Transparent; } }
        box.TextChanged += (_, _) => Preview(); Preview();
        button.Click += (_, _) =>
        {
            using var picker = new System.Windows.Forms.ColorDialog { FullOpen = true };
            try { var color = (Color)ColorConverter.ConvertFromString(box.Text); picker.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B); } catch { }
            if (picker.ShowDialog(new ColorOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) == System.Windows.Forms.DialogResult.OK)
                box.Text = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
        };
        var row = (Grid)box.Parent; row.ColumnDefinitions.Add(new() { Width = new GridLength(38) }); Grid.SetColumn(button, 2); row.Children.Add(button); return box;
    }
    public void ColorToggle(string enabledKey, string colorKey, string label, bool enabled, string color)
    {
        var box = ColorField(colorKey, label, color); var row = (Grid)box.Parent;
        row.Children.RemoveAt(0);
        var toggle = new CheckBox { Content = label, IsChecked = enabled, Margin = new(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        fields[enabledKey] = toggle; row.Children.Insert(0, toggle);
    }
    private sealed record ColorOwner(IntPtr Handle) : System.Windows.Forms.IWin32Window;
    public void Element(UIElement element) => current.Children.Add(element);
    public void ActionButton(string title, Action action)
    {
        var button = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { SetError(ex.Message); } };
        current.Children.Add(button);
    }

    public static string? Prompt(Window owner, string title, string label, string value = "")
    {
        var dialog = new FormDialog(title, 640, 380) { Owner = owner }; dialog.Section("Параметры"); dialog.Text("value", label, value);
        dialog.Validate = () => dialog.Value("value").Length > 0 || dialog.SetError("Введите значение.");
        return dialog.ShowDialog() == true ? dialog.Value("value") : null;
    }
    public static void ShowText(Window owner, string title, string text)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 730, Height = 460, MinWidth = 440, MinHeight = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(12) }; dialog.Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var copy = new Button { Content = "Копировать", MinWidth = 100, ToolTip = "Скопировать весь текст" };
        copy.Click += async (_, _) => await ClipboardService.CopyAsync(dialog, text, () => copy.Content = "Скопировано");
        var close = new Button { Content = "Закрыть", MinWidth = 100, IsCancel = true }; close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(copy); buttons.Children.Add(close);
        root.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.ShowDialog();
    }
}
