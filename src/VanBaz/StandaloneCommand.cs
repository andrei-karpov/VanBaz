using System.Diagnostics;
using System.IO;

namespace VanBaz;

public static class StandaloneCommand
{
    public static ProcessStartInfo Build(CommandPreset command)
    {
        if (command.Kind == "Параметры 1С") throw new InvalidOperationException("Выберите базу для применения параметров.");
        string target = command.Target.Trim();
        if (command.Kind == "Web-адрес")
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("Укажите HTTP(S)-адрес.");
        }
        else if (!Path.IsPathFullyQualified(target) || (command.Kind == "Каталог" ? !Directory.Exists(target) : !File.Exists(target))) throw new IOException("Команда не найдена: " + target);
        if (command.Kind == "Программа" && !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Выберите программу .exe.");
        if (command.WorkingDirectory.Length > 0 && !Directory.Exists(command.WorkingDirectory)) throw new DirectoryNotFoundException("Рабочая папка не найдена.");
        return new ProcessStartInfo(target) { UseShellExecute = true, Arguments = command.Kind is "Web-адрес" or "Каталог" ? "" : command.Arguments, WorkingDirectory = command.WorkingDirectory };
    }
}
