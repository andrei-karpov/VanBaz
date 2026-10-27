using System.IO;
using System.Windows;

namespace VanBaz;

public partial class MainWindow
{
    private FormDialog CreateReconcileSourceDialog()
    {
        var f = new FormDialog("Согласование списка баз", 800, 480); f.Section("Источник");
        f.BrowseText("path", "Штатный или внешний .v8i", CatalogReconciler.StandardList, false, "Список 1С|*.v8i");
        f.Check("writeExternal", "Разрешить запись во внешний список по выбранному пути", false);
        f.Note("Штатный ibases.v8i допускает оба направления. Внешние и общие списки по умолчанию только читаются; запись в них нужно разрешить отдельно для этой операции.");
        f.Note("Сначала будет показан план. Существующие различия без сохранённого исходного состояния считаются конфликтами. Автоматической записи нет."); f.SetAcceptText("Сравнить");
        f.Validate = () => File.Exists(f.Value("path")) || f.SetError("Выберите существующий файл списка.");
        return f;
    }
    private async void Reconcile_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || isolatedSession) return;
        var f = CreateReconcileSourceDialog(); f.Owner = this;
        if (f.ShowDialog() != true) return;
        string operation = "reconcile:" + Guid.NewGuid(); operations.Add(operation);
        try
        {
            string path = f.Value("path"); var snapshot = catalog.Copy();
            bool canWrite = CatalogReconciler.IsStandardList(path) || f.Flag("writeExternal");
            var plan = await Task.Run(() => CatalogReconciler.Prepare(snapshot, path, canWrite));
            var review = new ReconcileWindow(plan) { Owner = this };
            if (review.ShowDialog() != true) return;
            var result = CatalogReconciler.Apply(catalog, store, plan); catalog = result.Catalog; BuildRows();
            StatusText.Text = "Согласование завершено." + (result.Backup == null ? " Файл 1С не изменён." : " Копия: " + result.Backup);
            if (result.Warning != null) { StatusText.Text = result.Warning; MessageBox.Show(this, result.Warning, "Согласование: журнал восстановления", MessageBoxButton.OK, MessageBoxImage.Warning); }
            await RefreshOfficialListsAsync();
        }
        catch (Exception ex) { Error("Согласование списка", ex); }
        finally { operations.Remove(operation); }
    }
}
