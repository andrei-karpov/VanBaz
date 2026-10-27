using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public sealed class ApplicationLockWindow : Window
{
    public TextBlock HintText { get; } = new() { Text = "Для продолжения работы в ВанБаз введите пароль", Margin = new(10, 5, 7, 5), TextWrapping = TextWrapping.Wrap };
    public PasswordBox Password { get; } = new() { Height = 23 };
    public Button Accept { get; } = new() { Content = "OK", IsDefault = true, Width = 105, Height = 25 };
    public Slider Transparency { get; } = new() { Minimum = 130, Maximum = 255, Value = 255, ToolTip = "Непрозрачность окна", Width = 144, VerticalAlignment = VerticalAlignment.Center };
    public ApplicationLockWindow()
    {
        Title = "ВанБаз заблокирован"; Width = 375; SizeToContent = SizeToContent.Height;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var content = new StackPanel(); Content = content;
        content.Children.Add(HintText);
        var row = new DockPanel { Margin = new(13, 0, 13, 5) }; row.Children.Add(new TextBlock { Text = "Пароль:", Width = 51, VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(Password); content.Children.Add(row);
        var buttons = new DockPanel { Margin = new(3, 1, 12, 5) }; content.Children.Add(buttons);
        DockPanel.SetDock(Transparency, Dock.Left); buttons.Children.Add(Transparency);
        var help = new Button { Content = "?", Width = 35, Height = 25, ToolTip = "Справка о временной блокировке" };
        help.Click += (_, _) => MessageBox.Show(this, "Введите пароль, заданный перед блокировкой. Ползунок меняет прозрачность этого окна. Блокировка действует только внутри ВанБаз и не заменяет блокировку сеанса Windows.", "Временная блокировка", MessageBoxButton.OK, MessageBoxImage.Information);
        DockPanel.SetDock(help, Dock.Right); buttons.Children.Add(help); buttons.Children.Add(Accept);
        Transparency.ValueChanged += (_, _) => Opacity = Transparency.Value / 255;
        Loaded += (_, _) => Password.Focus();
    }
}
