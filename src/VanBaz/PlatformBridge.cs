using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace VanBaz;

// A separate COM host keeps platform loading and blocked connections outside the UI process.
// Requests (including optional credentials) go through stdin, never arguments or files.
public static class PlatformBridge
{
    public static string Connection(BaseEntry entry, string accessCode = "")
    {
        if (entry.Kind is not ("File" or "Server") || entry.Version.StartsWith("7."))
            throw new InvalidOperationException("Внешнее соединение поддерживает файловую или серверную базу 1С 8.x.");
        string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        string result = entry.ConnectionDisplay;
        if (entry.DesignerLogin && entry.DesignerUser.Length > 0)
        {
            result += "Usr=" + Quote(entry.DesignerUser) + ";";
            if (entry.DesignerSecret.Length > 0) result += "Pwd=" + Quote(SecretStore.Reveal(entry.DesignerSecret)) + ";";
        }
        if (entry.WindowsAuthentication != "Авто") result += "WA=" + (entry.WindowsAuthentication == "Да" ? "1" : "0") + ";";
        if (accessCode.Length > 0) result += "UC=" + Quote(accessCode) + ";";
        return result;
    }
    public static async Task<BridgeResult> Execute(BaseEntry entry, string operation, string architecture, string accessCode = "", SessionLockState? value = null, SessionLockState? expected = null)
    {
        if (operation is not ("metadata" or "get-lock" or "set-lock")) throw new ArgumentException("Неизвестная операция.");
        if (operation != "metadata" && entry.Kind != "File") throw new InvalidOperationException("Этот диалог предназначен для файловых баз.");
        if (architecture is not ("x86" or "x64")) throw new ArgumentException("Выберите разрядность COMConnector.");
        if (operation != "metadata" && entry.Version.StartsWith("8.1")) throw new InvalidOperationException("Блокировка через COM доступна начиная с 1С 8.2.");
        if (operation == "set-lock" && (value == null || expected == null)) throw new ArgumentException("Сначала прочитайте текущее состояние блокировки.");
        var request = new { Connection = Connection(entry, accessCode), Operation = operation, ProgId = entry.Version.StartsWith("8.2") ? "V82.COMConnector" : entry.Version.StartsWith("8.1") ? "V81.COMConnector" : "V83.COMConnector", Value = value, Expected = expected };
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string host = Path.Combine(windows, architecture == "x86" ? @"SysWOW64\WindowsPowerShell\v1.0\powershell.exe" : @"System32\WindowsPowerShell\v1.0\powershell.exe");
        var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Не удалось запустить внешний COM-хост.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            throw new IOException(operation == "set-lock" ? "COM-хост не ответил. Состояние блокировки неизвестно: прочитайте его повторно перед следующей операцией." : "COM-хост не ответил за 2 минуты. Проверьте регистрацию COMConnector, разрядность и доступ к базе.");
        }
        string output = await stdout; _ = await stderr; // Do not expose COM exception strings that might contain a connection or password.
        if (process.ExitCode != 0 || output.Length == 0) throw new IOException("Внешнее соединение не выполнено. Нужен зарегистрированный COMConnector подходящей версии и разрядности, доступ к базе и права администратора базы. Код процесса: " + process.ExitCode);
        var result = JsonSerializer.Deserialize<BridgeResult>(output) ?? throw new IOException("Пустой ответ платформы.");
        if (!result.Ok) throw new IOException("Внешнее соединение не завершило операцию: " + result.Stage + ", HRESULT " + result.Code + ". " + (result.Stage is "write-lock" or "verify-lock" ? "Состояние блокировки неизвестно: прочитайте его повторно." : result.Stage == "changed-lock" ? "Блокировка изменилась после чтения. Откройте диалог заново." : "Проверьте права, регистрацию COMConnector и параметры подключения."));
        return result;
    }
    private const string Script = """
        $ErrorActionPreference='Stop'
        [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        $stage='COMConnector'
        function Read-LockState($item) {
          return @{Enabled=[bool]$item.Use;Begin=([datetime]$item.Begin).ToString('yyyy-MM-ddTHH:mm:ss');End=([datetime]$item.End).ToString('yyyy-MM-ddTHH:mm:ss');Message=[string]$item.Message;KeyCode=[string]$item.KeyCode;Parameter=[string]$item.Parameter}
        }
        try {
          $q=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadToEnd())) | ConvertFrom-Json
          $connector=New-Object -ComObject $q.ProgId
          $stage='Connect'
          $connection=$connector.Connect($q.Connection)
          $stage=$q.Operation
          if ($q.Operation -eq 'metadata') {
            $m=$connection.Metadata
            $result=@{Ok=$true;Name=[string]$m.Name;Version=[string]$m.Version}
          } else {
            $lock=$connection.GetSessionsLock()
            if ($q.Operation -eq 'set-lock') {
              $current=Read-LockState $lock
              foreach($field in @('Enabled','Begin','End','Message','KeyCode','Parameter')) {
                if ([string]$current[$field] -cne [string]$q.Expected.$field) { $stage='changed-lock'; throw 'Changed' }
              }
              $lock.Use=[bool]$q.Value.Enabled
              $lock.Begin=[datetime]::ParseExact($q.Value.Begin,'yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
              $lock.End=[datetime]::ParseExact($q.Value.End,'yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
              $lock.Message=[string]$q.Value.Message
              $lock.KeyCode=[string]$q.Value.KeyCode
              $stage='write-lock'
              $connection.SetSessionsLock($lock)
              $stage='verify-lock'
              [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($lock)
              $lock=$connection.GetSessionsLock()
            }
            $result=@{Ok=$true;Lock=(Read-LockState $lock)}
          }
          [Console]::Write(($result | ConvertTo-Json -Depth 5 -Compress))
        } catch {
          [Console]::Write((@{Ok=$false;Stage=$stage;Code=('0x{0:X8}' -f $_.Exception.HResult)} | ConvertTo-Json -Compress))
        } finally {
          foreach($object in @($m,$lock,$connection,$connector)) {
            if ($null -ne $object -and [Runtime.InteropServices.Marshal]::IsComObject($object)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($object) }
          }
        }
        """;
}
public class BridgeResult
{
    public bool Ok { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Code { get; set; } = "";
    public SessionLockState? Lock { get; set; }
}
public class SessionLockState
{
    public bool Enabled { get; set; }
    public string Begin { get; set; } = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
    public string End { get; set; } = DateTime.Now.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss");
    public string Message { get; set; } = "Выполняется обслуживание информационной базы.";
    public string KeyCode { get; set; } = "";
    public string Parameter { get; set; } = "";
}
