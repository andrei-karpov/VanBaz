using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VanBaz;

public sealed class ParameterExamples(Func<IEnumerable<CommandPreset>> read, Func<CommandPreset?, CommandPreset?, bool>? change = null)
{
    public static ParameterExamples Empty { get; } = new(() => Array.Empty<CommandPreset>());
    public void Attach(FormDialog owner, TextBox arguments) => owner.ActionButton("Примеры параметров…", () =>
    {
        var dialog = CreateDialog(owner, arguments);
        dialog.ShowDialog();
    });
    public FormDialog CreateDialog(Window owner, TextBox? arguments = null)
    {
        var form = new FormDialog("Примеры параметров запуска 1С", 760, 610) { Owner = owner };
        form.Section("Примеры параметров");
        var search = form.Text("search", "Найти пример", "");
        var list = new ListBox { Height = 185, DisplayMemberPath = "Name", Margin = new(0, 6, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetName(list, "Примеры параметров запуска"); form.Element(list);
        var preview = form.Text("preview", "Параметры", "", true); preview.IsReadOnly = true; preview.MinHeight = 64;
        list.SelectionChanged += (_, _) => { preview.Text = (list.SelectedItem as CommandPreset)?.Arguments ?? ""; };
        void Refresh(CommandPreset? selected = null)
        {
            selected ??= list.SelectedItem as CommandPreset;
            list.ItemsSource = read().Where(c => c.Kind == "Параметры 1С" && (c.Name + " " + c.Arguments).Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();
            list.SelectedItem = selected; if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
        }
        search.TextChanged += (_, _) => Refresh(); Refresh();
        if (change != null)
        {
            var buttons = new WrapPanel(); form.Element(buttons);
            Button Add(string title, Action action) { var b = new Button { Content = title }; b.Click += (_, _) => action(); buttons.Children.Add(b); return b; }
            void Edit(CommandPreset? original)
            {
                var editor = new FormDialog(original == null ? "Новый пример параметров" : "Изменить пример параметров", 680, 350) { Owner = form };
                editor.Section("Пример"); editor.Text("name", "Название", original?.Name ?? ""); editor.Text("args", "Параметры 1С", original?.Arguments ?? "", true);
                editor.Note("Не сохраняйте здесь пароли и коды разрешения. Подставляемые значения проверьте в форме запуска.");
                editor.Validate = () => editor.Value("name").Length > 0 && editor.Value("args").Length > 0 || editor.SetError("Введите название и параметры.");
                if (editor.ShowDialog() == true)
                { var value = new CommandPreset { Name = editor.Value("name"), Arguments = editor.Value("args") }; if (change(original, value)) Refresh(value); }
            }
            Add("Добавить…", () => Edit(null));
            var edit = Add("Изменить…", () => { if (list.SelectedItem is CommandPreset c) Edit(c); });
            var delete = Add("Удалить…", () =>
            {
                if (list.SelectedItem is CommandPreset c && MessageBox.Show(form, "Удалить пример «" + c.Name + "»? Параметры баз сохранятся.", "Примеры параметров", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes && change(c, null)) Refresh();
            });
            DialogBehavior.Selection(list, edit, delete);
            form.Note("Изменения списка примеров сохраняются сразу.");
        }
        form.Note(arguments == null ? "Примеры доступны рядом с дополнительными параметрами в свойствах базы и профиля запуска." : "Выбранный пример будет вставлен в поле дополнительных параметров. Проверьте значения перед сохранением или запуском.");
        form.SetAcceptText(arguments == null ? "Закрыть" : "Вставить");
        form.Validate = () =>
        {
            if (arguments == null) return true;
            if (list.SelectedItem is not CommandPreset c) return form.SetError("Выберите пример.");
            Insert(arguments, c.Arguments); return true;
        };
        list.MouseDoubleClick += (_, e) =>
        {
            if (arguments != null && e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListBoxItem && form.Validate()) form.DialogResult = true;
        };
        return form;
    }
    public static void Insert(TextBox target, string example)
    {
        int start = target.SelectionStart, end = start + target.SelectionLength;
        string prefix = start > 0 && !char.IsWhiteSpace(target.Text[start - 1]) ? " " : "";
        string suffix = end < target.Text.Length && !char.IsWhiteSpace(target.Text[end]) ? " " : "";
        string text = prefix + example.Trim() + suffix;
        target.SelectedText = text; target.Select(start + text.Length, 0); target.Focus();
    }
}
