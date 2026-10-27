using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VanBaz;

public sealed class ReconcileWindow : Window
{
    public ReconcileWindow(ReconcilePlan plan)
    {
        Title = "Согласование со списком 1С"; Width = 1050; Height = 670; MinWidth = 780; MinHeight = 490; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource(typeof(Window)); UseLayoutRounding = true;
        var root = new DockPanel { Margin = new(12) }; Content = root;
        var header = new TextBlock { Text = plan.Path + "\n" + (CatalogReconciler.IsStandardList(plan.Path) ? "Штатный список 1С. " : "Внешний список. ") + (plan.CanWriteSource ? "Запись разрешена по выбранным действиям." : "Только чтение: изменения применяются к ВанБаз.") + "\nУдаление касается регистрации; файлы баз не затрагиваются. В подробностях виден адрес подключения; остальные значения Connect и дополнительные ключи скрыты.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column, ItemsSource = plan.Rows };
        grid.Columns.Add(new DataGridTextColumn { Header = "База / группа", Binding = new Binding("Name"), IsReadOnly = true, MinWidth = 210, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Различие", Binding = new Binding("Change"), IsReadOnly = true, Width = 245 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Действие", ItemsSource = plan.AllowedChoices, SelectedItemBinding = new Binding("Choice") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 215 });
        var detail = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 195, Margin = new(0, 10, 0, 0), FontFamily = new("Consolas"), FontSize = 12 };
        DockPanel.SetDock(detail, Dock.Bottom); root.Children.Add(detail); root.Children.Add(grid);
        grid.SelectionChanged += (_, _) => detail.Text = (grid.SelectedItem as ReconcileRow)?.Detail ?? "";
        var cancel = new Button { Content = "Отмена", IsCancel = true }; cancel.Click += (_, _) => DialogResult = false; footer.Children.Add(cancel);
        var apply = new Button { Content = "Применить выбранные действия", IsDefault = true }; footer.Children.Add(apply);
        apply.Click += (_, _) => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); DialogResult = true; };
        if (plan.Rows.Count > 0) grid.SelectedIndex = 0; else detail.Text = "Различий нет. Можно сохранить исходное состояние для следующего сравнения.";
    }
}
