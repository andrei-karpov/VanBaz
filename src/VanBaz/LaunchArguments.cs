using System.IO;

namespace VanBaz;

public sealed record LaunchArgument(string Key, string? Value)
{
    public string Family => new[] { "/WA", "/WSA", "/AppAutoCheckVersion" }.FirstOrDefault(k => Key.Equals(k + "+", StringComparison.OrdinalIgnoreCase) || Key.Equals(k + "-", StringComparison.OrdinalIgnoreCase)) ?? Key;
    public bool Secret => new[] { "/P", "/UC", "/WSP", "/AccessToken", "/ConfigurationRepositoryP", "/IBConnectionString" }.Contains(Key, StringComparer.OrdinalIgnoreCase);
}

// Longest recognised name wins; remaining text is a value for attached forms such as /Lru.
public static class LaunchArguments
{
    private static readonly string[] Values = { "/IBConnectionString", "/IBName", "/ConfigurationRepositoryF", "/ConfigurationRepositoryN", "/ConfigurationRepositoryP", "/AllowExecuteScheduledJobs", "/AccessToken", "/MainWindowMode", "/AppArch", "/DebuggerURL", "/SLev", "/Out", "/Execute", "/WSN", "/WSP", "/UC", "/F", "/S", "/WS", "/N", "/P", "/L", "/C", "/Z", "/O" };
    private static readonly string[] Flags = { "/WA+", "/WA-", "/WSA+", "/WSA-", "/ClearCache", "/iTaxi", "/itdi", "/isdi", "/AppAutoCheckMode", "/AppAutoCheckVersion+", "/AppAutoCheckVersion-", "/RunModeManagedApplication", "/RunModeOrdinaryApplication", "/DisableStartupMessages", "/DisableStartupDialogs", "/DisableUnrecoverableErrorMessage", "/DisplayPerformance", "/TechnicalSpecialistMode", "/EnableCheckExtensionsAndAddIns", "/SafeMode", "/TestManager", "/TestClient", "/UsePrivilegedMode" };
    private static readonly string[] Forbidden = { "/DumpIB", "/RestoreIB", "/DumpCfg", "/LoadCfg", "/UpdateCfg", "/UpdateDBCfg", "/IBCheckAndRepair", "/CreateInfobase", "/DeleteCfg", "/LoadConfigFromFiles", "/LoadConfigFromFilesByList", "/LoadExternalDataProcessorOrReportFromFiles" };
    private static readonly string[] Options = Flags.Append("/Debug").ToArray();
    private static readonly string[] Ordered = Values.Concat(Options).Concat(Forbidden).OrderByDescending(x => x.Length).ToArray();
    public static bool TakesValue(string token) => Values.Contains(token, StringComparer.OrdinalIgnoreCase);
    public static List<LaunchArgument> Parse(string text)
    {
        var tokens = LaunchService.SplitArguments(text); var result = new List<LaunchArgument>();
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            string? key = Ordered.FirstOrDefault(k => token.Equals(k, StringComparison.OrdinalIgnoreCase));
            key ??= Ordered.FirstOrDefault(k => token.StartsWith(k, StringComparison.OrdinalIgnoreCase) && !Options.Contains(k));
            if (key == null) throw new ArgumentException("Неизвестный дополнительный ключ. Используйте параметры формы или поддерживаемые ключи из справки профиля.");
            if (Forbidden.Contains(key)) throw new ArgumentException("Обслуживание базы выполняется отдельной командой, а не дополнительными ключами запуска.");
            string? value = null;
            if (Values.Contains(key))
            {
                if (token.Length > key.Length) { value = token[key.Length..]; if (value.StartsWith('=')) value = value[1..]; }
                else if (++i < tokens.Length) value = tokens[i];
                else throw new ArgumentException("Не задано значение ключа " + key + ".");
                if (value.Length == 0 || value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) throw new ArgumentException("Пустое или многострочное значение ключа " + key + ".");
                if (key != "/C" && value.StartsWith('/')) throw new ArgumentException("Вместо значения ключа " + key + " указан другой ключ. Проверьте параметры.");
            }
            result.Add(new(key, value));
            if (key == "/Debug")
            {
                string? mode = null; bool attach = false;
                while (i + 1 < tokens.Length && tokens[i + 1].StartsWith('-'))
                {
                    string modifier = tokens[++i].ToLowerInvariant();
                    if (modifier is "-http" or "-tcp" && mode == null) mode = modifier;
                    else if (modifier == "-attach" && !attach) attach = true;
                    else throw new ArgumentException("Для /Debug допустим один режим -tcp или -http и необязательный -attach.");
                }
                if (attach && mode != "-http") throw new ArgumentException("Модификатор -attach используется только с /Debug -http.");
                if (mode != null) result.Add(new(mode, null));
                if (attach) result.Add(new("-attach", null));
            }
            // /Out has one optional modifier. Keep it attached to this option's value.
            if (key == "/Out" && i + 1 < tokens.Length && tokens[i + 1].Equals("-NoTruncate", StringComparison.OrdinalIgnoreCase)) { result.Add(new("-NoTruncate", null)); i++; }
        }
        return result;
    }
    public static string Help => "Поддерживаются отдельные, слитные и key=value формы ключей с параметром.\n\n" + string.Join(" ", Values) + "\n\n" + string.Join(" ", Options) + "\n\n/Debug допускается без значения; необязательно: -tcp либо -http, затем -attach только для -http. /Out допускает -NoTruncate после имени файла.\n\nПодключение /F, /S, /WS, /IBConnectionString, /IBName задаётся выбранной базой и запрещено в дополнительных ключах. Неизвестные ключи отклоняются. Секреты вводите в защищённых полях.";
    public static IEnumerable<string> Keys(IEnumerable<string> arguments)
    {
        var tokens = arguments.ToArray();
        for (int i = 0; i < tokens.Length; i++)
        {
            var key = Values.Concat(Options).FirstOrDefault(k => k.Equals(tokens[i], StringComparison.OrdinalIgnoreCase));
            if (key == null) continue;
            yield return key; if (Values.Contains(key)) i++;
        }
    }
    public static void Append(System.Diagnostics.ProcessStartInfo info, string text)
    {
        var extras = Parse(text);
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Existing arguments have already been built as distinct key/value pairs.
        for (int i = 1; i < info.ArgumentList.Count; i++)
        {
            string token = info.ArgumentList[i]; var key = Values.Concat(Options).FirstOrDefault(k => k.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (key == null) continue;
            occupied.Add(new LaunchArgument(key, null).Family); if (Values.Contains(key)) i++;
        }
        foreach (var arg in extras)
        {
            if (new[] { "/F", "/S", "/WS", "/IBConnectionString", "/IBName" }.Contains(arg.Key)) throw new ArgumentException("Второе подключение в дополнительных ключах запрещено. Измените свойства выбранной базы.");
            if (!occupied.Add(arg.Family)) throw new ArgumentException("Ключ " + arg.Key + " задан несколько раз. Уберите дублирование с полями формы.");
        }
        if (new[] { "/AppAutoCheckMode", "/RunModeManagedApplication", "/RunModeOrdinaryApplication" }.Count(occupied.Contains) > 1) throw new ArgumentException("Выберите один режим клиента в форме запуска.");
        if (new[] { "/iTaxi", "/itdi", "/isdi" }.Count(occupied.Contains) > 1) throw new ArgumentException("Выберите один режим интерфейса.");
        foreach (var arg in extras) { info.ArgumentList.Add(arg.Key); if (arg.Value != null) info.ArgumentList.Add(arg.Value); }
    }
}
