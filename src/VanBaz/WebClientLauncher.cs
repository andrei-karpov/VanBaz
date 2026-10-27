using System.Diagnostics;
using System.IO;

namespace VanBaz;

public static class WebClientLauncher
{
    public static ProcessStartInfo Build(BaseEntry entry, string browser, string? executable = null)
    {
        if (entry.Kind != "Web" || !Uri.TryCreate(entry.Location, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new InvalidOperationException("Нужна веб-публикация HTTP/HTTPS без имени и пароля в адресе.");
        string relative = browser switch { "Chrome" => @"Google\Chrome\Application\chrome.exe", "Edge" => @"Microsoft\Edge\Application\msedge.exe", _ => throw new ArgumentException("Выберите Chrome или Edge.") };
        executable ??= new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData }
            .Select(p => Path.Combine(Environment.GetFolderPath(p), relative)).FirstOrDefault(File.Exists);
        if (executable == null || !File.Exists(executable)) throw new FileNotFoundException("Выбранный браузер не найден: " + browser);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false }; info.ArgumentList.Add(uri.AbsoluteUri); return info;
    }
}
