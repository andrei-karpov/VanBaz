using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace VanBaz;

public sealed record ReleaseCheckSelection(string CurrentVersion, bool Local, string Address, string ConfigurationId, ReleaseInfo Release);

public sealed class BaseReleaseWindow : Window
{
    public TextBlock ResultText { get; }
    public TextBlock StatusText { get; }
    public TextBox AddressBox { get; }
    public TextBox CurrentVersionBox { get; }
    public RadioButton LocalSource { get; }
    public Button CheckButton { get; }
    public Button SaveButton { get; }
    public Button DistributionsButton { get; }

    public BaseReleaseWindow(BaseEntry entry, IEnumerable<ConfigurationSource> configurations,
        Func<string, bool, CancellationToken, Task<ReleaseInfo>> read,
        Func<ReleaseCheckSelection, bool> save,
        Action<Window, ConfigurationSource, string>? distributions = null)
    {
        Title = "Проверка релиза · " + entry.Name; Width = 770; Height = 580; MinWidth = 520; MinHeight = 370;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; DialogBehavior.FitToScreen(this);
        var sources = configurations.Select(c => c.Copy()).ToList();
        var initialSource = sources.FirstOrDefault(c => c.Id == entry.ConfigurationId);
        string onlineAddress = initialSource?.InfoUrl ?? entry.UpdateInfoUrl;
        string localAddress = entry.LocalUpdateInfoFile;
        bool local = entry.UseLocalUpdateInfo, busy = false, closed = false;
        CancellationTokenSource? cancellation = null;
        ReleaseCheckSelection? result = null;
        ConfigurationSource? resultSource = null;
        var root = new DockPanel { Margin = new(14) }; Content = root;
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        StatusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(StatusText, Dock.Bottom); root.Children.Add(StatusText);
        var content = new StackPanel(); root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var sourcesPanel = new StackPanel(); content.Children.Add(sourcesPanel);
        sourcesPanel.Children.Add(new TextBlock { Text = "Текущая версия конфигурации", Margin = new(0, 0, 0, 4) });
        CurrentVersionBox = new TextBox { Name = "ReleaseCurrentVersion", Text = entry.ConfigurationVersion }; sourcesPanel.Children.Add(CurrentVersionBox);
        sourcesPanel.Children.Add(new TextBlock { Text = "Конфигурация из списка", Margin = new(0, 12, 0, 4) });
        var choice = new ComboBox { Name = "ReleaseConfiguration", ItemsSource = sources.Prepend(new ConfigurationSource { Id = "", Name = "Свой источник" }).ToList(), DisplayMemberPath = "Name" };
        choice.SelectedItem = initialSource ?? choice.Items[0]; sourcesPanel.Children.Add(choice);
        var modes = new WrapPanel { Margin = new(0, 14, 0, 7) }; sourcesPanel.Children.Add(modes);
        var online = new RadioButton { Name = "ReleaseOnlineSource", Content = "В Интернете (HTTPS)", GroupName = "ReleaseSource", IsChecked = !local, Margin = new(0, 0, 20, 0) }; modes.Children.Add(online);
        LocalSource = new RadioButton { Name = "ReleaseLocalSource", Content = "Локальный UpdInfo.txt", GroupName = "ReleaseSource", IsChecked = local }; modes.Children.Add(LocalSource);
        var addressRow = new DockPanel(); sourcesPanel.Children.Add(addressRow);
        var browse = new Button { Name = "BrowseReleaseSource", Content = "Файл…", IsEnabled = local }; DockPanel.SetDock(browse, Dock.Right); addressRow.Children.Add(browse);
        AddressBox = new TextBox { Name = "ReleaseSourceAddress", Text = local ? localAddress : onlineAddress, ToolTip = "Источник сведений о релизе" }; addressRow.Children.Add(AddressBox);
        content.Children.Add(new Separator { Margin = new(0, 14, 0, 12) });
        ResultText = new TextBlock { Name = "BaseReleaseResult", Text = "Выберите источник сведений о релизе.", TextWrapping = TextWrapping.Wrap }; content.Children.Add(ResultText);
        content.Children.Add(new TextBlock { Name = "ReleaseChanges", Text = "Описание изменений не получено. Смотрите сведения поставщика.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 15, 0, 0) });
        CheckButton = new Button { Name = "CheckBaseRelease", Content = "Проверить · F5", IsDefault = true }; footer.Children.Add(CheckButton);
        DistributionsButton = new Button { Name = "BaseReleaseDistributions", Content = "Дистрибутив релиза…", IsEnabled = false }; footer.Children.Add(DistributionsButton);
        SaveButton = new Button { Name = "SaveBaseReleaseSource", Content = "Сохранить", IsEnabled = false, ToolTip = "Запомнить источник, текущую версию и результат проверки" }; footer.Children.Add(SaveButton);
        var close = new Button { Name = "CloseBaseRelease", Content = "Закрыть", IsCancel = true }; close.Click += (_, _) => Close(); footer.Children.Add(close);
        void States()
        {
            sourcesPanel.IsEnabled = !busy; CheckButton.IsEnabled = !busy;
            SaveButton.IsEnabled = !busy && result != null;
            DistributionsButton.IsEnabled = !busy && result is { Local: false } && resultSource != null && distributions != null;
        }
        void Invalidate()
        {
            result = null; resultSource = null; ResultText.Text = "Источник или версия изменены. Повторите проверку."; StatusText.Text = ""; States();
        }
        void Mode(bool value)
        {
            if (local == value) return;
            if (local) localAddress = AddressBox.Text; else onlineAddress = AddressBox.Text;
            local = value; AddressBox.Text = local ? localAddress : onlineAddress; browse.IsEnabled = local; Invalidate();
        }
        online.Checked += (_, _) => Mode(false); LocalSource.Checked += (_, _) => Mode(true);
        AddressBox.TextChanged += (_, _) => Invalidate(); CurrentVersionBox.TextChanged += (_, _) => Invalidate();
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedItem is ConfigurationSource source && source.Id.Length > 0)
            { onlineAddress = source.InfoUrl; online.IsChecked = true; AddressBox.Text = onlineAddress; }
            Invalidate();
        };
        browse.Click += (_, _) => { var dialog = new OpenFileDialog { Title = "Сведения о релизе", Filter = "UpdInfo.txt|UpdInfo.txt|Текстовые файлы|*.txt" }; if (dialog.ShowDialog(this) == true) AddressBox.Text = dialog.FileName; };
        async Task Check()
        {
            if (busy || closed) return;
            string address = AddressBox.Text.Trim(), current = CurrentVersionBox.Text.Trim();
            if (address.Length == 0) { StatusText.Text = "Укажите адрес или выберите файл UpdInfo.txt."; return; }
            bool localRequest = local;
            var selected = choice.SelectedItem as ConfigurationSource;
            result = null; resultSource = null; busy = true; cancellation = new(); States(); StatusText.Text = "Чтение сведений о релизе…";
            try
            {
                var info = await read(address, localRequest, cancellation.Token);
                if (closed) return;
                string comparison = ReleaseService.Compare(info, current);
                result = new(current, localRequest, address, selected?.InfoUrl == address ? selected.Id : "", info);
                if (!localRequest)
                {
                    resultSource = selected?.Copy() ?? new ConfigurationSource { Name = entry.Name };
                    if (resultSource.InfoUrl != address) resultSource.DownloadRoot = "";
                    resultSource.InfoUrl = address;
                }
                ResultText.Text = $"{comparison}\n\nТекущая версия: {(current.Length > 0 ? current : "не задана")}\nРелиз в источнике: {info.Version}\nДата релиза: {(info.Date.Length > 0 ? info.Date : "не указана")}\nОбновление с версий: {(info.FromVersions.Length > 0 ? info.FromVersions : "не указаны")}";
                StatusText.Text = localRequest ? "Проверен локальный файл. Адрес дистрибутива по нему не определяется. Нажмите «Сохранить», чтобы запомнить результат." : "Проверка завершена. Дистрибутив проверяется по отдельному списку поставщика. Нажмите «Сохранить», чтобы запомнить результат.";
            }
            catch (OperationCanceledException) { if (!closed) StatusText.Text = "Проверка отменена или истёк срок ожидания."; }
            catch (Exception ex) { if (!closed) { ResultText.Text = "Сведения не получены."; StatusText.Text = ex.Message; } }
            finally { busy = false; cancellation.Dispose(); cancellation = null; if (!closed) States(); }
        }
        CheckButton.Click += async (_, _) => await Check();
        SaveButton.Click += (_, _) =>
        {
            if (busy || result == null) return;
            try { StatusText.Text = save(result) ? "Источник и результат сохранены." : "Изменения не сохранены."; }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        };
        DistributionsButton.Click += (_, _) =>
        {
            if (busy || result is not { Local: false } || resultSource == null) return;
            try { distributions?.Invoke(this, resultSource.Copy(), result.Release.Version); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        };
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.F5) { e.Handled = true; await Check(); } };
        Loaded += async (_, _) => { if (AddressBox.Text.Trim().Length > 0) await Check(); };
        Closed += (_, _) => { closed = true; cancellation?.Cancel(); };
        States();
    }
}
