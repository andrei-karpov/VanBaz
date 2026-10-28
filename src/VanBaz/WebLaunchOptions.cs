using System.Diagnostics;

namespace VanBaz;

public class WebLaunchOptions
{
    public string Speed { get; set; } = "По умолчанию";
    public string Authentication { get; set; } = "По умолчанию";
    public string User { get; set; } = "";
    public string Proxy { get; set; } = "По умолчанию";
    public string ProxyServer { get; set; } = "";
    public int ProxyPort { get; set; } = 8080;
    public string ProxyUser { get; set; } = "";
    public static void Append(ProcessStartInfo plan, BaseEntry entry)
    {
        if (entry.Kind != "Web") return;
        var settings = entry.WebOptions ?? new();
        void Add(string name, string value) { if (value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) throw new ArgumentException("Недопустимый параметр веб-подключения."); plan.ArgumentList.Add(name); plan.ArgumentList.Add(value); }
        if (settings.Speed is "Низкая" or "Обычная") Add("/O", settings.Speed == "Низкая" ? "Low" : "Normal");
        else if (settings.Speed == "Выбирать при запуске") throw new InvalidOperationException("Выберите скорость соединения перед запуском.");
        if (settings.Authentication == "Запрашивать") plan.ArgumentList.Add("/WSA-");
        else if (settings.Authentication == "Windows") plan.ArgumentList.Add("/WSA+");
        if (settings.User.Length > 0) Add("/WSN", settings.User);
        if (settings.Proxy == "Без прокси") plan.ArgumentList.Add("/NoProxy");
        else if (settings.Proxy == "Указать")
        {
            if (string.IsNullOrWhiteSpace(settings.ProxyServer) || settings.ProxyPort is < 1 or > 65535) throw new ArgumentException("Укажите сервер прокси и порт от 1 до 65535.");
            plan.ArgumentList.Add("/Proxy"); Add("-PSrv", settings.ProxyServer); Add("-PPort", settings.ProxyPort.ToString()); if (settings.ProxyUser.Length > 0) Add("-PUser", settings.ProxyUser);
        }
    }
}

public partial class MainWindow
{
    private bool ChooseConnectionSpeed(BaseEntry entry)
    {
        if (entry.Kind != "Web" || entry.WebOptions?.Speed != "Выбирать при запуске") return true;
        var form = new FormDialog("Скорость соединения · " + entry.Name, 540, 310) { Owner = this }; form.Section("Подключение");
        form.Choice("speed", "Скорость", "Обычная", new[] { "Обычная", "Низкая" }); form.SetAcceptText("Запустить");
        if (form.ShowDialog() != true) return false; entry.WebOptions.Speed = form.Value("speed"); return true;
    }
}
