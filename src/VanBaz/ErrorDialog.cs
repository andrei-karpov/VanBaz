using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public sealed class ErrorDialog : Window
{
    public string SelectedAction { get; private set; } = "";
    public ErrorDialog(string title, Exception error, bool applicationActions = false)
    {
        Title = title; Width = 620; SizeToContent = SizeToContent.Height; MinHeight = 190; MaxHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner; DialogBehavior.FitToScreen(this);
        var root = new DockPanel { Margin = new Thickness(14) }; Content = root;
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var body = new StackPanel(); root.Children.Add(body);
        body.Children.Add(new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        string details = title + "\n\n" + error;
        body.Children.Add(new Expander { Header = "Подробности ошибки", Content = new TextBox { Name = "ErrorDetails", Text = details, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) } });
        var copy = new Button { Content = "Копировать" }; copy.Click += async (_, _) => await ClipboardService.CopyAsync(this, details, () => status.Text = "Подробности скопированы.", message => status.Text = message); buttons.Children.Add(copy);
        if (applicationActions)
            foreach (var item in new[] { ("restart", "Перезапустить"), ("exit", "Завершить ВанБаз") })
            { var button = new Button { Content = item.Item2 }; string action = item.Item1; button.Click += (_, _) => { SelectedAction = action; Close(); }; buttons.Children.Add(button); }
        var close = new Button { Content = "Закрыть", IsDefault = true, IsCancel = true }; close.Click += (_, _) => Close(); buttons.Children.Add(close);
    }
}
