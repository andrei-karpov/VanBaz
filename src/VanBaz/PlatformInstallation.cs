using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace VanBaz;
public sealed record PlatformInstallation(string ProductCode, string Source)
{
    public ProcessStartInfo Uninstall() => new("msiexec.exe") { UseShellExecute = true, ArgumentList = { "/x", ProductCode } };
    public static PlatformInstallation? Find(PlatformInfo platform)
    {
        var exact = new List<PlatformInstallation>(); var version = new List<PlatformInstallation>();
        var view = platform.Architecture == "x86" ? RegistryView.Registry32 : RegistryView.Registry64;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var root = RegistryKey.OpenBaseKey(hive,view).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (root == null) continue;
            foreach (string name in root.GetSubKeyNames())
            {
                if (!Guid.TryParse(name, out var product)) continue;
                using var key = root.OpenSubKey(name); if (key == null) continue;
                string title = key.GetValue("DisplayName") as string ?? "", publisher = key.GetValue("Publisher") as string ?? "";
                if (!(title.Contains("1C:Enterprise",StringComparison.OrdinalIgnoreCase) || title.Contains("1С:Предприятие",StringComparison.OrdinalIgnoreCase)) || !publisher.Contains("1C",StringComparison.OrdinalIgnoreCase) && !publisher.Contains("1С",StringComparison.OrdinalIgnoreCase)) continue;
                if (key.GetValue("DisplayVersion") as string != platform.Version) continue;
                var item = new PlatformInstallation("{"+product+"}",key.GetValue("InstallSource") as string ?? ""); version.Add(item);
                string location = key.GetValue("InstallLocation") as string ?? "";
                if (location.Length > 0 && Path.IsPathFullyQualified(location) && platform.Directory.StartsWith(Path.GetFullPath(location).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)) exact.Add(item);
            }
        }
        return exact.Distinct().Count() == 1 ? exact[0] : version.Distinct().Count() == 1 ? version[0] : null;
    }
}
