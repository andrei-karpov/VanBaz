using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace VanBaz;
public partial class MainWindow
{
    private ParameterExamples CreateParameterExamples() => new(() => catalog.Commands, (original, value) => CanEdit && Commit(() =>
    {
        int index = original == null ? -1 : catalog.Commands.IndexOf(original);
        if (value == null) { if (index >= 0) catalog.Commands.RemoveAt(index); }
        else if (index < 0) catalog.Commands.Add(value); else catalog.Commands[index] = value;
    }));
    private FormDialog CreateExternalCommandsDialog(Window owner)
    {
        var form = new FormDialog("Внешние команды", 720, 460) { Owner = owner }; form.Section("Внешние команды");
        var list = new ListBox { Height = 220, DisplayMemberPath = "Name" }; form.Element(list);
        void Refresh() { var selected = list.SelectedItem; list.ItemsSource = catalog.Commands.Where(c => c.Kind != "Параметры 1С").ToList(); list.SelectedItem = selected; if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0; }
        var row = new WrapPanel(); form.Element(row);
        Button Add(string text, Action action) { var b = new Button { Content = text }; row.Children.Add(b); b.Click += (_, _) => action(); return b; }
        void Edit(CommandPreset? command) { var editor = CreateCommandDialog(command); editor.Owner = form; editor.ShowDialog(); Refresh(); }
        Add("Добавить…", () => Edit(null));
        var edit = Add("Изменить…", () => { if (list.SelectedItem is CommandPreset c) Edit(c); });
        var delete = Add("Удалить…", () => { if (list.SelectedItem is CommandPreset c && MessageBox.Show(form, "Удалить команду «" + c.Name + "»?", "Внешние команды", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes && Commit(() => catalog.Commands.Remove(c))) Refresh(); });
        var run = Add("Выполнить", () => { if (!isolatedSession && list.SelectedItem is CommandPreset c) try { Process.Start(StandaloneCommand.Build(c)); } catch (Exception ex) { form.SetError(ex.Message); } });
        list.MouseDoubleClick += (_, e) => { if (DialogBehavior.OnItem(e.OriginalSource) && list.SelectedItem is CommandPreset c) Edit(c); };
        DialogBehavior.Selection(list, edit, delete, run); Refresh();
        form.Note("Программы, папки, документы и ссылки. Изменения сохраняются сразу. Примеры ключей 1С находятся в параметрах запуска."); form.SetAcceptText("Закрыть"); return form;
    }
}
