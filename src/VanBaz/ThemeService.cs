using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VanBaz;

public sealed class ThemeDefinition
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public Dictionary<string, string> Colors { get; set; } = new();
    public ThemeDefinition Copy() => JsonSerializer.Deserialize<ThemeDefinition>(JsonSerializer.Serialize(this))!;
}

public static class ThemeService
{
    public static readonly string[] ColorKeys = { "Window", "Surface", "Text", "Muted", "Border", "Button", "Hover", "Accent", "Selection", "SelectionText", "Group", "Error" };
    private static ThemeDefinition Light(string name, string background, string accent = "#267FAF") => new()
    {
        Name = name, Colors = new()
        {
            ["Window"] = background, ["Surface"] = "#FFFFFF", ["Text"] = "#202020", ["Muted"] = "#686868",
            ["Border"] = "#C8C8C8", ["Button"] = "#FAFAFA", ["Hover"] = "#E4EDF4", ["Accent"] = accent,
            ["Selection"] = "#FFE733", ["SelectionText"] = "#111111", ["Group"] = "#F4F4F4", ["Error"] = "#B92323"
        }
    };
    public static List<ThemeDefinition> All(Preferences p)
    {
        var list = new List<ThemeDefinition>
        {
            Light("Светло-серый", "#F2F2F2"), Light("Голубой", "#E8F0F6"), Light("Песочный", "#F3EEE2"),
            Light("Зелёный", "#E8F0E8"), Light("Белый", "#FFFFFF"),
            new() { Name = "Тёмный", Colors = new()
            {
                ["Window"] = "#25282D", ["Surface"] = "#1C1F23", ["Text"] = "#E8EBEF", ["Muted"] = "#ABB3BF",
                ["Border"] = "#535A64", ["Button"] = "#343A43", ["Hover"] = "#465363", ["Accent"] = "#80C7F0",
                ["Selection"] = "#365F85", ["SelectionText"] = "#FFFFFF", ["Group"] = "#2C3239", ["Error"] = "#FF9393"
            }}
        };
        foreach (var theme in p.Themes)
        {
            Validate(theme);
            list.RemoveAll(t => t.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase)); list.Add(theme);
        }
        return list;
    }
    public static ThemeDefinition Selected(Preferences p) => All(p).FirstOrDefault(t => t.Name == p.Theme) ?? All(p)[0];
    public static void Validate(ThemeDefinition theme)
    {
        if (string.IsNullOrWhiteSpace(theme.Name) || theme.Name.Length > 80) throw new InvalidDataException("Название темы: 1–80 символов.");
        foreach (string key in ColorKeys)
            if (!theme.Colors.TryGetValue(key, out var value) || value.Length != 7 || value[0] != '#' ||
                !uint.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException("Некорректный цвет темы: " + key + ". Нужен #RRGGBB.");
    }
    public static void Upsert(Preferences p, ThemeDefinition theme)
    {
        Validate(theme);
        p.Themes.RemoveAll(t => t.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase));
        p.Themes.Add(theme);
    }
    private static Color ColorOf(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static Color Blend(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R * (1 - amount) + b.R * amount), (byte)Math.Round(a.G * (1 - amount) + b.G * amount), (byte)Math.Round(a.B * (1 - amount) + b.B * amount));
    private static double Luminance(Color c)
    {
        double Linear(byte v) { double n = v / 255.0; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
        return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
    }
    private static double Contrast(Color a, Color b) => (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    public static string Readable(string color, string backgroundKey = "ThemeSurface")
    {
        var background = (Application.Current.TryFindResource(backgroundKey) as SolidColorBrush)?.Color ?? Colors.White;
        Color foreground;
        try { foreground = ColorOf(color); } catch { foreground = Colors.Black; }
        Color target = Luminance(background) < .3 ? Colors.White : Colors.Black;
        for (int step = 0; step < 20 && Contrast(foreground, background) < 4.5; step++) foreground = Blend(foreground, target, .15);
        return Hex(foreground);
    }
    public static string TextColor => Hex(((SolidColorBrush)Application.Current.FindResource("ThemeText")).Color);
    public static void Apply(Preferences p)
    {
        var theme = Selected(p);
        var colors = theme.Colors.ToDictionary(k => k.Key, k => ColorOf(k.Value));
        // VCL GrayText is intended for disabled controls; our notes must remain readable.
        Color mutedTarget = Luminance(colors["Window"]) < .3 ? Colors.White : Colors.Black;
        for (int step = 0; step < 20 && Contrast(colors["Muted"], colors["Window"]) < 4.5; step++)
            colors["Muted"] = Blend(colors["Muted"], mutedTarget, .15);
        colors["Selection"] = Blend(colors["Surface"], colors["Selection"], Math.Clamp(p.SelectionIntensity, 0, 255) / 255.0);
        if (Contrast(colors["Selection"], colors["SelectionText"]) < 4.5)
            colors["SelectionText"] = Luminance(colors["Selection"]) < .3 ? Colors.White : Colors.Black;
        var resources = Application.Current.Resources;
        foreach (var pair in colors) { var brush = new SolidColorBrush(pair.Value); brush.Freeze(); resources["Theme" + pair.Key] = brush; }
        void SystemBrush(ResourceKey key, string color) => resources[key] = resources["Theme" + color];
        SystemBrush(SystemColors.WindowBrushKey, "Surface"); SystemBrush(SystemColors.WindowTextBrushKey, "Text");
        SystemBrush(SystemColors.ControlBrushKey, "Window"); SystemBrush(SystemColors.ControlTextBrushKey, "Text");
        SystemBrush(SystemColors.GrayTextBrushKey, "Muted"); SystemBrush(SystemColors.HighlightBrushKey, "Selection");
        SystemBrush(SystemColors.HighlightTextBrushKey, "SelectionText");
        SystemBrush(SystemColors.InactiveSelectionHighlightBrushKey, "Selection"); SystemBrush(SystemColors.InactiveSelectionHighlightTextBrushKey, "SelectionText");
        SystemBrush(SystemColors.MenuBrushKey, "Surface"); SystemBrush(SystemColors.MenuTextBrushKey, "Text");
    }
    public static ThemeDefinition Read(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Файл темы больше 16 МБ.");
        if (Path.GetExtension(path).Equals(".vsf", StringComparison.OrdinalIgnoreCase)) return ReadVsf(File.ReadAllBytes(path), Path.GetFileName(path));
        var result = JsonSerializer.Deserialize<ThemeDefinition>(File.ReadAllText(path)) ?? throw new InvalidDataException("Пустая тема.");
        Validate(result); return result;
    }
    public static ThemeDefinition ReadVsf(byte[] file, string source = "")
    {
        byte[] header = Encoding.ASCII.GetBytes("VCL_STYLE 2.0");
        if (file.Length < 15 || file.Length > 16 * 1024 * 1024 || !file.AsSpan(0, header.Length).SequenceEqual(header))
            throw new InvalidDataException("Поддерживаются палитры VCL_STYLE 2.0 (.vsf).");
        using var input = new MemoryStream(file, header.Length, file.Length - header.Length);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = zlib.Read(buffer)) > 0)
        {
            if (output.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("Распакованная тема больше 32 МБ.");
            output.Write(buffer, 0, count);
        }
        byte[] raw = output.ToArray();
        string StringAt(ref int position, int max)
        {
            if (position < 0 || position > raw.Length - 4) throw new InvalidDataException("Повреждена таблица темы.");
            int length = BitConverter.ToInt32(raw, position); position += 4;
            if (length < 1 || length > max || length > (raw.Length - position) / 2) throw new InvalidDataException("Повреждена строка темы.");
            string value = Encoding.Unicode.GetString(raw, position, length * 2); position += length * 2; return value;
        }
        int namePosition = 0;
        var theme = Light(StringAt(ref namePosition, 80), "#F2F2F2");
        theme.Source = source; theme.SourceHash = Convert.ToHexString(SHA256.HashData(file));
        string? Value(string key)
        {
            byte[] signature = BitConverter.GetBytes(key.Length).Concat(Encoding.Unicode.GetBytes(key)).ToArray();
            int offset = 0;
            while (offset < raw.Length)
            {
                int found = raw.AsSpan(offset).IndexOf(signature); if (found < 0) return null;
                offset += found + signature.Length;
                int position = offset;
                try { if (StringAt(ref position, 1) == ":") return StringAt(ref position, 128); }
                catch (InvalidDataException) { }
            }
            return null;
        }
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["clBlack"] = "#000000", ["clWhite"] = "#FFFFFF", ["clGray"] = "#808080", ["clSilver"] = "#C0C0C0",
            ["clRed"] = "#FF0000", ["clBlue"] = "#0000FF", ["clGreen"] = "#008000", ["clNavy"] = "#000080",
            ["clMaroon"] = "#800000", ["clYellow"] = "#FFFF00", ["clTeal"] = "#008080", ["clAqua"] = "#00FFFF",
            ["clFuchsia"] = "#FF00FF", ["clPurple"] = "#800080", ["clOlive"] = "#808000", ["clLime"] = "#00FF00"
        };
        string? Palette(string key)
        {
            var value = Value(key);
            if (value == null) return null;
            if (names.TryGetValue(value, out var color)) return color;
            if (value.StartsWith('$') && uint.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) && n <= 0xFFFFFF)
                return $"#{n & 255:X2}{n >> 8 & 255:X2}{n >> 16 & 255:X2}";
            return null;
        }
        foreach (var key in new[] { "clWindow", "clWindowText", "clBtnFace", "clHighlight" })
            if (Palette(key) == null) throw new InvalidDataException("Не найдена поддерживаемая палитра .vsf: " + key);
        void Map(string target, params string[] keys)
        {
            foreach (string key in keys) if (Palette(key) is { } color) { theme.Colors[target] = color; return; }
        }
        Map("Window", "Window", "clBtnFace"); Map("Surface", "clWindow"); Map("Text", "clWindowText");
        Map("Muted", "clGrayText"); Map("Border", "clWindowFrame"); Map("Button", "ButtonNormal", "clBtnFace");
        Map("Hover", "ButtonHot", "clBtnHighlight"); Map("Selection", "clHighlight");
        // VCL's clDefault is a sentinel, not an RGB value. Use normal text and enforce contrast when applying.
        Map("SelectionText", "clHighlightText", "clWindowText");
        Map("Group", "Panel", "clBtnFace");
        Validate(theme); return theme;
    }
    public static int ImportFolder(Preferences p, string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Папка тем не найдена.");
        var themes = Directory.EnumerateFiles(folder, "*.vsf").OrderBy(f => f).Select(Read).ToList();
        foreach (var theme in themes) Upsert(p, theme);
        return themes.Count;
    }
}
