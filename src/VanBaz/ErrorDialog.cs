using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public sealed class ErrorDialog : Window
{
    public string SelectedAction { get; private set; } = "";
    public ErrorDialog(string title, Exception error, bool applicationActions = false, bool fatal = false,
        Action<string>? clipboard = null, Action<string>? openLink = null)
    {
        Title = title; Width = 620; SizeToContent = SizeToContent.Height; MinHeight = 190; MaxHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner; DialogBehavior.FitToScreen(this);
        var root = new DockPanel { Margin = new Thickness(14) }; Content = root;
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var body = new StackPanel(); root.Children.Add(body);
        body.Children.Add(new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        if (fatal) body.Children.Add(new TextBlock { Text = "Работа будет завершена без дополнительного сохранения каталога. Несохранённые изменения этого сеанса могут быть потеряны.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        string details = title + "\n\n" + error;
        body.Children.Add(new Expander { Header = "Подробности ошибки", Content = new TextBox { Name = "ErrorDetails", Text = details, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) } });
        var copy = new Button { Name = "CopyError", Content = "Копировать", ToolTip = "Копировать сообщение и технические подробности ошибки" }; copy.Click += async (_, _) => await ClipboardService.CopyAsync(this, details, () => status.Text = "Подробности скопированы.", message => status.Text = message, clipboard); buttons.Children.Add(copy);
        var report = new Button { Name = "ReportError", Content = "Сообщить об ошибке", ToolTip = "Открыть GitHub ВанБаз. Сообщение и файлы не отправляются автоматически." };
        report.Click += (_, _) =>
        {
            try
            {
                const string url = "https://github.com/andrei-karpov/VanBaz/issues/new";
                if (openLink != null) openLink(url);
                else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                status.Text = "Открыта страница нового обращения. Перед отправкой проверьте скопированные подробности.";
            }
            catch (Exception ex) { status.Text = "Не удалось открыть страницу. " + ex.Message; }
        };
        buttons.Children.Add(report);
        if (applicationActions)
            foreach (var item in new[] { ("restart", "Перезапустить"), ("exit", "Завершить ВанБаз") })
            { var button = new Button { Name = item.Item1 == "restart" ? "RestartAfterError" : "ExitAfterError", Content = item.Item2 }; string action = item.Item1; button.Click += (_, _) => { SelectedAction = action; Close(); }; buttons.Children.Add(button); }
        var close = new Button { Name = "CloseError", Content = fatal ? "Завершить" : "Закрыть", IsDefault = true, IsCancel = true }; close.Click += (_, _) => Close(); buttons.Children.Add(close);
    }

    internal static string Present(string title, Exception error, Window? owner = null, bool applicationActions = false, bool fatal = false)
    {
        try
        {
            var dialog = new ErrorDialog(title, error, applicationActions, fatal);
            if (owner?.IsVisible == true) dialog.Owner = owner;
            dialog.ShowDialog(); return dialog.SelectedAction;
        }
        catch
        {
            // The original failure stays visible even if resources or the richer dialog cannot load.
            MessageBox.Show(title + "\n\n" + error.Message + (fatal ? "\n\nПрограмма будет завершена без дополнительного сохранения каталога." : ""), "ВанБаз", MessageBoxButton.OK, MessageBoxImage.Error);
            return "";
        }
    }
}
