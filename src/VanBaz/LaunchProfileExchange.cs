using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VanBaz;

public sealed class PortableLaunchProfile
{
    public string Format { get; set; } = "VanBaz.LaunchProfile";
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public bool Designer { get; set; }
    public string Version { get; set; } = "8.3";
    public string Architecture { get; set; } = "Авто";
    public string Client { get; set; } = "Авто";
    public string Language { get; set; } = "";
    public bool ClearCache { get; set; }
    public bool DisableScheduledJobs { get; set; }
    public bool Taxi { get; set; }
}
public static class LaunchProfileExchange
{
    public const string Notice = "Переносятся имя, режим, версия, разрядность, клиент, язык и переключатели. Пароли, код разрешения, пользователи, привязка к базе, закреплённый EXE, пути обработки/хранилища, /C, /Z и свободные аргументы исключаются. После импорта вход запрашивается средствами 1С; запуск от администратора отключён.";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static void Validate(PortableLaunchProfile p)
    {
        if (p.Format == null || !Regex.IsMatch(p.Format, @"^[A-Za-z][A-Za-z0-9]{0,63}\.LaunchProfile$") || p.SchemaVersion != 1 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 200 || p.Name.Any(char.IsControl) || p.Version == null || !Regex.IsMatch(p.Version, @"^(?:7\.7|8\.\d+(?:\.\d+){0,2})$")) throw new InvalidDataException("Неподдерживаемый или повреждённый профиль запуска.");
        if (!new[] { "Авто", "x86", "x64" }.Contains(p.Architecture) || !new[] { "Авто", "Обычный режим", "Толстый клиент", "Толстый управляемый", "Тонкий клиент" }.Contains(p.Client) || p.Language == null || !Regex.IsMatch(p.Language, @"^(?:[a-zA-Z]{2,3}(?:[-_][a-zA-Z]{2,3})?)?$")) throw new InvalidDataException("Недопустимые настройки переносимого профиля.");
    }
    public static void Export(LaunchProfile profile, string path)
    {
        var p = new PortableLaunchProfile { Name = profile.Name, Designer = profile.Designer, Version = profile.Version, Architecture = profile.Architecture, Client = profile.Client, Language = profile.Advanced.Language, ClearCache = profile.Advanced.ClearCache, DisableScheduledJobs = profile.Advanced.DisableScheduledJobs, Taxi = profile.Advanced.Taxi };
        Validate(p); CacheService.EnsureOrdinaryPath(path); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(p, Options)); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static LaunchProfile Import(string path)
    {
        CacheService.EnsureOrdinaryPath(path); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 256 * 1024) throw new InvalidDataException("Профиль превышает 256 КБ.");
        var p = JsonSerializer.Deserialize<PortableLaunchProfile>(stream, Options) ?? throw new InvalidDataException("Пустой профиль."); Validate(p);
        return new() { Name = p.Name, Designer = p.Designer, Version = p.Version, Architecture = p.Architecture, Client = p.Client, AskLogin = true, WindowsAuthentication = "Нет", Advanced = new() { Language = p.Language, ClearCache = p.ClearCache, DisableScheduledJobs = p.DisableScheduledJobs, Taxi = p.Taxi } };
    }
}
