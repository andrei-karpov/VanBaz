using System.Text.Json;

namespace VanBaz;

public sealed class AdvancedLaunch
{
    public string Language { get; set; } = "";
    public string StartupParameter { get; set; } = "";
    public string DataArea { get; set; } = "";
    public string PermissionSecret { get; set; } = "";
    public bool ClearCache { get; set; }
    public bool DisableScheduledJobs { get; set; }
    public bool Taxi { get; set; }
}
public sealed class LaunchProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string BaseId { get; set; } = "";
    public bool Designer { get; set; }
    public string Version { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Architecture { get; set; } = "Авто";
    public string Client { get; set; } = "Авто";
    public string WindowsAuthentication { get; set; } = "Авто";
    public bool AskLogin { get; set; }
    public string UserName { get; set; } = "";
    public string UserSecret { get; set; } = "";
    public bool RunAsAdmin { get; set; }
    public string ExternalProcessor { get; set; } = "";
    public string ExtraArguments { get; set; } = "";
    public bool UseRepository { get; set; }
    public string Repository { get; set; } = "";
    public string RepositoryUser { get; set; } = "";
    public string RepositorySecret { get; set; } = "";
    public AdvancedLaunch Advanced { get; set; } = new();
    public override string ToString() => Name + (Designer ? " · Конфигуратор" : " · Предприятие");
    public bool AppliesTo(BaseEntry entry) => (BaseId.Length == 0 || BaseId.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)) && (entry.Version == "7.7") == (Version == "7.7");
    public LaunchProfile CopyForRun()
    {
        var copy = JsonSerializer.Deserialize<LaunchProfile>(JsonSerializer.Serialize(this))!;
        copy.Id = Guid.NewGuid().ToString(); return copy;
    }
    public static LaunchProfile From(BaseEntry b, bool designer, string client) => new()
    {
        Version = b.Version, Executable = b.Executable, Architecture = b.Architecture, Client = client,
        Designer = designer, WindowsAuthentication = b.WindowsAuthentication, AskLogin = !(designer ? b.DesignerLogin : b.EnterpriseLogin),
        UserName = designer ? b.DesignerUser : b.UserName, UserSecret = designer ? b.DesignerSecret : b.EnterpriseSecret,
        RunAsAdmin = b.RunAsAdmin, ExternalProcessor = b.ExternalProcessor, ExtraArguments = b.ExtraArguments,
        UseRepository = b.UseRepository, Repository = b.Repository, RepositoryUser = b.RepositoryUser, RepositorySecret = b.RepositorySecret,
        Advanced = JsonSerializer.Deserialize<AdvancedLaunch>(JsonSerializer.Serialize(b.Advanced))!
    };
    public BaseEntry Apply(BaseEntry entry)
    {
        if (SchemaVersion != 1) throw new InvalidOperationException("Версия профиля запуска не поддерживается.");
        if (BaseId.Length > 0 && !BaseId.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Этот профиль закреплён за другой базой.");
        if (entry.Version == "7.7" != (Version == "7.7")) throw new InvalidOperationException("Профили 7.7 и 8.x несовместимы.");
        var b = entry.Copy(); b.Version = Version; b.Executable = Executable; b.Architecture = Architecture; b.Client = Client;
        b.WindowsAuthentication = AskLogin ? "Нет" : WindowsAuthentication; b.EnterpriseLogin = b.DesignerLogin = !AskLogin;
        if (Designer) { b.DesignerUser = UserName; b.DesignerSecret = AskLogin ? "" : UserSecret; } else { b.UserName = UserName; b.EnterpriseSecret = AskLogin ? "" : UserSecret; }
        b.RunAsAdmin = RunAsAdmin; b.ExternalProcessor = ExternalProcessor; b.ExtraArguments = ExtraArguments;
        b.UseRepository = UseRepository; b.Repository = Repository; b.RepositoryUser = RepositoryUser; b.RepositorySecret = RepositorySecret;
        b.Advanced = JsonSerializer.Deserialize<AdvancedLaunch>(JsonSerializer.Serialize(Advanced))!; return b;
    }
}
