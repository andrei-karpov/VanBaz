using System.Windows;
using System.Windows.Controls;

namespace VanBaz;
public sealed class ExplorerActionWindow : Window
{
    public string SelectedAction { get; private set; } = "";
    public ExplorerActionWindow(string file, bool canEdit, bool registered = true)
    {
        Title = "Открыть файловую базу"; Width = 500; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner; DialogBehavior.FitToScreen(this);
        var panel = new StackPanel { Margin = new Thickness(16) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = file, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        foreach (string action in new[] { "Зарегистрировать", "Предприятие", "Конфигуратор" })
        { var button = new Button { Content = action, IsEnabled = action == "Предприятие" && registered || canEdit, ToolTip = !registered && !canEdit ? "Сначала зарегистрируйте базу в режиме Специалист или Разработчик." : action, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 32 }; panel.Children.Add(button); button.Click += (_, _) => { SelectedAction = action; DialogResult = true; }; }
        if (!registered && !canEdit) panel.Children.Add(new TextBlock { Text = "Эта база ещё не зарегистрирована. Добавление доступно в режиме Специалист или Разработчик.", TextWrapping = TextWrapping.Wrap });
        var cancel = new Button { Content = "Отмена", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(cancel); cancel.Click += (_, _) => DialogResult = false;
    }
}
