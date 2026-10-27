using System.Diagnostics;
using System.IO;

namespace VanBaz;

public static class LaunchCapabilities
{
    public const string Help = "Проверяются: тонкий/управляемый клиент — семейство 8.2 и новее; Такси — 8.3 и новее и управляемый режим; запрет регламентных заданий — только файловое Предприятие 8.3 и новее. Конфигуратор не получает интерфейс, /Z, /Execute и /ClearCache. Точный минимальный релиз каждого дополнительного ключа не предполагается: для остальных сочетаний нужна проверка выбранной сборки.";
    public static void Validate(BaseEntry entry, bool designer, string client, string executable, IEnumerable<string> arguments, bool verifyExecutable = true)
    {
        string actual = verifyExecutable && File.Exists(executable) ? FileVersionInfo.GetVersionInfo(executable).FileVersion ?? "" : "";
        Version? version = Version.TryParse(actual, out var detected) && detected.Major == 8 ? detected : Version.TryParse(entry.Version.TrimEnd('*', '.'), out var requested) ? requested : null;
        ValidateKnown(entry.Kind, designer, client, version, LaunchArguments.Keys(arguments.Skip(1)));
    }
    public static void ValidateKnown(string kind, bool designer, string client, Version? version, IEnumerable<string> keys)
    {
        var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        bool Older(int minor) => version != null && (version.Major < 8 || version.Major == 8 && version.Minor < minor);
        if (Older(2) && (client is "Тонкий клиент" or "Толстый управляемый" || set.Contains("/RunModeManagedApplication"))) throw new ArgumentException("Тонкий и управляемый клиенты требуют платформу семейства 8.2 или новее.");
        if (client == "Тонкий клиент" && new[] { "/RunModeManagedApplication", "/RunModeOrdinaryApplication" }.Any(set.Contains)) throw new ArgumentException("Ключи толстого клиента несовместимы с выбранным тонким клиентом.");
        if (set.Contains("/iTaxi") && (Older(3) || client == "Обычный режим" || set.Contains("/RunModeOrdinaryApplication"))) throw new ArgumentException("Такси требует управляемый режим и платформу семейства 8.3 или новее.");
        if (set.Contains("/AllowExecuteScheduledJobs") && (designer || kind != "File" || Older(3))) throw new ArgumentException("Этот ключ запрета заданий предназначен только для файлового Предприятия 8.3 и новее. Он не управляет заданиями серверного кластера.");
        if (designer && new[] { "/iTaxi", "/itdi", "/isdi", "/Z", "/Execute", "/ClearCache", "/AppAutoCheckMode", "/RunModeOrdinaryApplication", "/RunModeManagedApplication" }.Any(set.Contains)) throw new ArgumentException("Ключи клиентского режима нельзя применять к Конфигуратору.");
        if (!designer && set.Any(k => k.StartsWith("/ConfigurationRepository", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Подключение хранилища доступно только Конфигуратору.");
    }
}
