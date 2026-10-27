using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace VanBaz;

public sealed class ProfileFilesWindow : Window
{
    public ProfileFilesWindow(ProfileContext context, string recoveryFolder)
    {
        Title = "Файлы профиля 1С"; Width = 1060; Height = 610; MinWidth = 800; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource(typeof(Window)); UseLayoutRounding = true;
        var root = new DockPanel { Margin = new(12) }; Content = root;
        var note = new TextBlock { Text = "База: " + (context.BaseId.Length == 0 ? "только общие настройки" : context.BaseId) + " · Пользователь 1С: " + (context.UserId.Length == 0 ? "не задан" : context.UserId) + "\nCtrl/Shift — выбор нескольких файлов. Перед сбросом или восстановлением закройте сеансы 1С. Архивы имеют собственный формат ВанБаз.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) }; DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        var footer = new WrapPanel { Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.Column };
        grid.Columns.Add(new DataGridTextColumn { Header = "Логический файл", Binding = new Binding("Key"), Width = 200 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Состояние", Binding = new Binding("State"), Width = 190 });
        var pathStyle = new Style(typeof(TextBlock)); pathStyle.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding("Path")));
        grid.Columns.Add(new DataGridTextColumn { Header = "Полный путь", Binding = new Binding("Path"), ElementStyle = pathStyle, MinWidth = 300, Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); root.Children.Add(grid);
        var detail = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 94, Margin = new(0, 8, 0, 0), Text = "Выберите файл, чтобы увидеть его назначение. Состав настроек зависит от версии платформы." };
        DockPanel.SetDock(detail, Dock.Bottom); root.Children.Remove(grid); root.Children.Add(detail); root.Children.Add(grid);
        grid.SelectionChanged += (_, _) => detail.Text = string.Join("\n\n", grid.SelectedItems.Cast<ProfileFile>().Select(f => f.Key + ": " + f.Description));
        void Refresh() => grid.ItemsSource = ProfileArchive.List(context);
        List<ProfileFile> Selected() => grid.SelectedItems.Cast<ProfileFile>().ToList();
        Button Add(string text, Action action) { var b = new Button { Content = text }; b.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Профили 1С", MessageBoxButton.OK, MessageBoxImage.Warning); } }; footer.Children.Add(b); return b; }
        var export = Add("Экспорт выбранных…", () =>
        {
            var items = Selected().Where(f => f.Exists).ToList(); if (items.Count == 0) throw new IOException("Выберите существующие файлы.");
            var save = new SaveFileDialog { Title = "Новый архив профилей ВанБаз", Filter = "Архив профилей|*.zip", FileName = "1c-profiles-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", OverwritePrompt = true };
            if (save.ShowDialog(this) == true) { ProfileArchive.Export(context, items.Select(f => f.Key), save.FileName, overwrite: true); MessageBox.Show(this, "Архив создан: " + save.FileName); }
        });
        var restore = Add("Восстановить выбранные…", () =>
        {
            var items = Selected(); if (items.Count == 0) return;
            var open = new OpenFileDialog { Title = "Архив профилей ВанБаз", Filter = "Архив профилей|*.zip" }; if (open.ShowDialog(this) != true) return;
            var read = ProfileArchive.Read(open.FileName);
            var available = items.Where(f => read.Data.ContainsKey(f.Key) || read.Manifest.AbsentFiles.Contains(f.Key)).ToList();
            if (available.Count == 0) throw new InvalidDataException("В архиве нет выбранных логических файлов. Укажите GUID пользователя, если восстанавливаются его настройки.");
            string mapping = string.Join("\n", available.Select(f => (read.Manifest.AbsentFiles.Contains(f.Key) ? "Убрать (до операции файла не было): " : "Восстановить: ") + f.Key + " → " + f.Path));
            if (MessageBox.Show(this, "Архив: " + read.Manifest.Edition + ", база " + read.Manifest.BaseId + ", пользователь " + read.Manifest.UserId + "\n\nБудут заменены или созданы:\n" + mapping + "\n\nПрочие файлы останутся прежними. Продолжить?", "План восстановления", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            string backup = ProfileArchive.Change(context, available.Select(f => f.Key), open.FileName, recoveryFolder); Refresh(); MessageBox.Show(this, "Восстановление завершено. Копия прежних файлов:\n" + backup);
        });
        var reset = Add("Сбросить выбранные…", () =>
        {
            var items = Selected().Where(f => f.Exists).ToList(); if (items.Count == 0) return;
            if (MessageBox.Show(this, "Сохранить файлы в архив восстановления и убрать текущие?\n\n" + string.Join("\n", items.Select(f => f.Path)), "Точечный сброс", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            string backup = ProfileArchive.Change(context, items.Select(f => f.Key), null, recoveryFolder); Refresh(); MessageBox.Show(this, "Файлы сохранены в архиве. Для возврата выберите те же строки и «Восстановить выбранные».\n\n" + backup);
        });
        Add("Обновить", Refresh); var close = Add("Закрыть", Close); close.IsCancel = true; DialogBehavior.Selection(grid, export, restore, reset); Refresh();
    }
}
