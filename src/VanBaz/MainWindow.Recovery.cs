using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace VanBaz;

public partial class MainWindow
{
    private List<RecoveryOperation> PendingRecovery() => FileRecovery.Pending(Path.Combine(store.Folder, "RecoveryOperations")).Concat(FileRecovery.Pending(Path.Combine(store.Folder, "ProfileRecovery", "RecoveryOperations"))).ToList();
    private bool HasPendingCatalogRecovery() => store.HasPendingRecovery();
    private void CheckPendingRecovery()
    {
        try { int count = PendingRecovery().Count; if (count > 0) StatusText.Text = "Есть незавершённые операции: " + count + ". Меню сервиса → Восстановление прерванных операций."; }
        catch (Exception ex) { StatusText.Text = "Проверка журнала восстановления: " + ex.Message; }
    }
    private void Recovery_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || isolatedSession) return;
        try
        {
            var pending = PendingRecovery();
            if (pending.Count == 0) { MessageBox.Show(this, "Незавершённых операций нет.", "Восстановление"); return; }
            var window = CreateRecoveryWindow(pending); window.Owner = this; window.ShowDialog();
        }
        catch (Exception ex) { Error("Восстановление операций", ex); }
    }
    private Window CreateRecoveryWindow(List<RecoveryOperation> pending)
    {
            var w = new Window { Title = "Восстановление прерванных операций", Width = 950, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var root = new DockPanel { Margin = new(12) }; w.Content = root;
            var info = new TextBlock { Text = "Возврат проверяет каждый файл. Если после операции файл меняли, он не будет перезаписан. Для PFL сначала закройте все сеансы 1С.", TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,10) }; DockPanel.SetDock(info, Dock.Top); root.Children.Add(info);
            var list = new ListBox { ItemsSource = pending, Height = 150 }; DockPanel.SetDock(list, Dock.Top); root.Children.Add(list);
            var bottom = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            var restore = new Button { Content = "Вернуть исходное состояние…" }; var accept = new Button { Content = "Подтвердить завершённую запись…" }; var close = new Button { Content = "Закрыть", IsCancel = true }; bottom.Children.Add(restore); bottom.Children.Add(accept); bottom.Children.Add(close); close.Click += (_, _) => w.Close();
            var detail = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0,10,0,10) }; root.Children.Add(detail);
            list.SelectionChanged += (_, _) => { try { detail.Text = list.SelectedItem is RecoveryOperation op ? FileRecovery.Preview(op) : ""; restore.IsEnabled = accept.IsEnabled = list.SelectedItem != null; } catch (Exception ex) { detail.Text = ex.Message; restore.IsEnabled = accept.IsEnabled = false; } };
            accept.Click += (_, _) =>
            {
                if (list.SelectedItem is not RecoveryOperation op) return;
                try
                {
                    if (MessageBox.Show(w, "Оставить результат операции? Все файлы должны точно совпадать с ожидаемым результатом. Изменится только отметка в журнале.", "Подтверждение результата", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    FileRecovery.AcceptApplied(op); pending = PendingRecovery(); list.ItemsSource = pending; if (pending.Count > 0) list.SelectedIndex = 0; else { detail.Text = "Результат операции подтверждён."; restore.IsEnabled = accept.IsEnabled = false; }
                }
                catch (Exception ex) { MessageBox.Show(w, ex.Message, "Результат не подтверждён"); }
            };
            restore.Click += (_, _) =>
            {
                if (list.SelectedItem is not RecoveryOperation op) return;
                try
                {
                    if (op.Kind.Contains("PFL") && CacheService.ClientsRunning()) throw new InvalidOperationException("Закройте сеансы 1С перед возвратом PFL.");
                    string preview = FileRecovery.Preview(op);
                    if (MessageBox.Show(w, preview + "\n\nВернуть исходное состояние этих файлов?", "План восстановления", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    FileRecovery.Restore(op); catalog = store.Load(); ApplyPreferences(); BuildRows(); pending = PendingRecovery(); list.ItemsSource = pending; if (pending.Count > 0) list.SelectedIndex = 0; else { detail.Text = "Возврат завершён."; restore.IsEnabled = accept.IsEnabled = false; }
                }
                catch (Exception ex) { MessageBox.Show(w, ex.Message, "Возврат не завершён"); }
            };
            list.SelectedIndex = 0; return w;
    }

}
