using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace VanBaz;

public static class Legacy77Service
{
    public static List<BaseEntry> ReadRegistered()
    {
        var result = new List<BaseEntry>();
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            using var key = hive.OpenSubKey(@"Software\1C\1Cv7\7.7\Titles", false);
            if (key == null) continue;
            foreach (string path in key.GetValueNames())
            {
                if (!Path.IsPathFullyQualified(path) || key.GetValue(path) is not string name || name.Length == 0) continue;
                string full = Path.GetFullPath(path).TrimEnd('\\');
                string id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant()))[..16]).ToString();
                if (result.Any(b => b.Id == id)) continue;
                result.Add(new() { Id = id, Name = name, Version = "7.7", Kind = "File", Location = full, Group = "1С:Предприятие 7.7", Architecture = "x86", EnterpriseLogin = false, DesignerLogin = false });
            }
        }
        return result;
    }
    public static ProcessStartInfo Build(BaseEntry entry, bool designer, bool includeExtras, bool requireInstalled = true)
    {
        if (entry.Kind != "File") throw new InvalidOperationException("Для 1С 7.7 укажите каталог базы.");
        string exe = entry.Executable;
        if (!Path.IsPathFullyQualified(exe) || !Path.GetFileName(exe).StartsWith("1cv7", StringComparison.OrdinalIgnoreCase) || !Path.GetExtension(exe).Equals(".exe", StringComparison.OrdinalIgnoreCase) || requireInstalled && !File.Exists(exe))
            throw new InvalidOperationException("Выберите установленный 1cv7*.exe в свойствах базы 7.7.");
        var info = new ProcessStartInfo(exe) { UseShellExecute = entry.RunAsAdmin, WorkingDirectory = Path.GetDirectoryName(exe)! };
        if (entry.RunAsAdmin) info.Verb = "runas";
        info.ArgumentList.Add(designer ? "config" : "enterprise");
        info.ArgumentList.Add("/D" + entry.Location);
        string user = designer ? entry.DesignerUser : entry.UserName;
        string secret = designer ? entry.DesignerSecret : entry.EnterpriseSecret;
        if ((designer ? entry.DesignerLogin : entry.EnterpriseLogin) && user.Length > 0)
        { info.ArgumentList.Add("/N" + user); if (secret.Length > 0) info.ArgumentList.Add("/P" + SecretStore.Reveal(secret)); }
        if (includeExtras) foreach (var argument in LaunchService.SplitArguments(entry.ExtraArguments)) info.ArgumentList.Add(argument);
        return info;
    }
}
