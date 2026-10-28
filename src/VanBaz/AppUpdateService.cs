using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace VanBaz;

public class AppPackage
{
    public string Version { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new();
}
public record PreparedAppUpdate(string Folder, string Version);
public static class AppUpdateService
{
    public static readonly string[] Files = { "VanBaz.exe", "VanBaz.dll", "VanBaz.deps.json", "VanBaz.runtimeconfig.json" };
    public static PreparedAppUpdate Prepare(string package, string workFolder)
    {
        using var zip = ZipFile.OpenRead(package);
        if (zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count || Files.Any(f => zip.GetEntry(f) == null) || zip.Entries.Any(e => !Files.Contains(e.FullName) && e.FullName is not ("vanbaz-package.json" or "README.txt"))) throw new InvalidDataException("Ожидался компактный ZIP ВанБаз: четыре файла программы, README.txt и/или vanbaz-package.json, без вложенных папок.");
        var manifestEntry = zip.GetEntry("vanbaz-package.json");
        if (manifestEntry?.Length > 65536 || zip.Entries.Sum(e => e.Length) > 256 * 1024 * 1024) throw new InvalidDataException("Превышен допустимый размер пакета.");
        AppPackage? manifest = null;
        if (manifestEntry != null)
        {
            using var input = manifestEntry.Open(); manifest = JsonSerializer.Deserialize<AppPackage>(input) ?? throw new InvalidDataException("Повреждён манифест пакета.");
            if (!Version.TryParse(manifest.Version, out var version) || version.Build < 0 || manifest.Files == null || manifest.Files.Count != Files.Length || Files.Any(f => !manifest.Files.TryGetValue(f, out var hash) || hash == null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))) throw new InvalidDataException("Повреждён список файлов пакета.");
        }
        string stage = Path.Combine(Path.GetFullPath(workFolder), "update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            foreach (string file in Files)
            {
                var entry = zip.GetEntry(file)!; string path = Path.Combine(stage, file);
                using (var input = entry.Open()) using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[65536]; int n; long total = 0;
                    while ((n = input.Read(buffer)) > 0) { total += n; if (total > 128 * 1024 * 1024) throw new IOException("Файл пакета слишком велик."); output.Write(buffer, 0, n); }
                    output.Flush(true);
                }
                using var content = File.OpenRead(path);
                if (manifest != null && !Convert.ToHexString(SHA256.HashData(content)).Equals(manifest.Files[file], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Контрольная сумма не совпадает: " + file);
            }
            var assembly = AssemblyName.GetAssemblyName(Path.Combine(stage, "VanBaz.dll"));
            if (assembly.Name != "VanBaz" || assembly.Version == null || manifest != null && assembly.Version.ToString(3) != new Version(manifest.Version).ToString(3)) throw new InvalidDataException("Сборка не является ВанБаз или её версия отличается от манифеста.");
            PlatformCatalog.Architecture(Path.Combine(stage, "VanBaz.exe"));
            using (JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "VanBaz.runtimeconfig.json")))) { }
            using (JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "VanBaz.deps.json")))) { }
            manifest ??= ProgramVersions.Manifest(stage);
            File.WriteAllText(Path.Combine(stage, "vanbaz-package.json"), JsonSerializer.Serialize(manifest));
            return new(stage, manifest.Version);
        }
        catch
        {
            // Only exact filenames created inside the unique staging folder are removed.
            foreach (string file in Files.Append("vanbaz-package.json")) { string path = Path.Combine(stage, file); if (File.Exists(path)) File.Delete(path); }
            if (!Directory.EnumerateFileSystemEntries(stage).Any()) Directory.Delete(stage);
            throw;
        }
    }
    public static async Task StartInstaller(PreparedAppUpdate prepared, string dataFolder, string mode = "Разработчик", bool lockMode = false)
    {
        if (PortableDistribution.IncludesRuntime(AppContext.BaseDirectory)) throw new IOException("Для портативной сборки скачайте полный ZIP из Releases. Закройте ВанБаз и распакуйте обновление в папку программы, сохранив Data.");
        string executable = Environment.ProcessPath ?? throw new IOException("Не найден путь ВанБаз.");
        if (Path.GetFileName(executable) != "VanBaz.exe") throw new IOException("Обновление доступно из VanBaz.exe.");
        string root = Path.GetDirectoryName(executable)!;
        ProgramVersions.Validate(prepared.Folder);
        ProgramVersions.Archive(root, dataFolder);
        ProgramVersions.Archive(prepared.Folder, dataFolder);
        prepared = ProgramVersions.Stage(prepared, dataFolder);
        var restart = RestartPlan.Create(executable, dataFolder, mode, lockMode);
        var plan = new { Root = root, Stage = prepared.Folder, Data = Path.GetFullPath(dataFolder), Arguments = string.Join(" ", restart.ArgumentList.Select(DesktopIntegration.Quote)), Pid = Environment.ProcessId, Started = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, Files };
        string script = Path.Combine(prepared.Folder, "install.ps1"), request = Path.Combine(prepared.Folder, "install.json");
        File.WriteAllText(script, Installer, new System.Text.UTF8Encoding(true)); File.WriteAllText(request, JsonSerializer.Serialize(plan));
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-PlanPath", request }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Не удалось запустить установщик.");
        for (int i = 0; i < 50; i++)
        {
            if (File.Exists(Path.Combine(prepared.Folder, "ready"))) return;
            if (process.HasExited) break;
            await Task.Delay(100);
        }
        File.WriteAllText(Path.Combine(prepared.Folder, "cancel"), "cancel");
        throw new IOException("Установщик не подтвердил готовность. Приложение остаётся открытым. Проверьте политику PowerShell и журнал result.txt в " + prepared.Folder);
    }
    private const string Installer = """
        param([string]$PlanPath)
        $ErrorActionPreference='Stop'
        $plan=Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
        $result=Join-Path $plan.Stage 'result.txt'
        $previous=Join-Path $plan.Stage 'Previous'
        $changed=@()
        try {
          $allowed=@('VanBaz.exe','VanBaz.dll','VanBaz.deps.json','VanBaz.runtimeconfig.json')
          if (@(Compare-Object $allowed $plan.Files).Count -ne 0) { throw 'Unexpected file list' }
          $manifest=Get-Content -LiteralPath (Join-Path $plan.Stage 'vanbaz-package.json') -Raw | ConvertFrom-Json
          foreach($name in $allowed) {
            $path=Join-Path $plan.Stage $name
            $stream=[IO.File]::OpenRead($path); $sha=[Security.Cryptography.SHA256]::Create()
            try { $hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ($hash -ne $manifest.Files.$name) { throw 'Package hash mismatch' }
            $target=Join-Path $plan.Root $name
            if ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse point target' }
          }
          $parent=Get-Process -Id $plan.Pid -ErrorAction Stop
          if ($parent.StartTime.ToUniversalTime().Ticks -ne $plan.Started) { throw 'Process identity changed' }
          [IO.File]::WriteAllText((Join-Path $plan.Stage 'ready'),'ready')
          if (-not $parent.WaitForExit(120000)) { throw 'Application did not exit; no files replaced' }
          if (Test-Path -LiteralPath (Join-Path $plan.Stage 'cancel')) { throw 'Installation cancelled; no files replaced' }
          New-Item -ItemType Directory -Path $previous -ErrorAction Stop | Out-Null
          foreach($name in $allowed) { Copy-Item -LiteralPath (Join-Path $plan.Root $name) -Destination (Join-Path $previous $name) -ErrorAction Stop }
          foreach($name in $allowed) {
            $target=Join-Path $plan.Root $name
            $pending=Join-Path $plan.Root ('.update-'+[guid]::NewGuid().ToString('N'))
            try { Copy-Item -LiteralPath (Join-Path $plan.Stage $name) -Destination $pending; [IO.File]::Replace($pending,$target,(Join-Path $previous ($name+'.replace.bak'))); $changed+=$name }
            finally { if(Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending } }
          }
          [IO.File]::WriteAllText($result,'Installed. Previous program files: '+$previous)
          Start-Process -FilePath (Join-Path $plan.Root 'VanBaz.exe') -ArgumentList $plan.Arguments -WindowStyle Hidden
        } catch {
          $failure=$_.Exception.Message
          foreach($name in $changed) { try { Copy-Item -LiteralPath (Join-Path $previous $name) -Destination (Join-Path $plan.Root $name) -Force } catch { $failure+='; rollback: '+$_.Exception.Message } }
          [IO.File]::WriteAllText($result,$failure)
        }
        """;
}
