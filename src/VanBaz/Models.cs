using System.Text.Json.Serialization;
using System.Text.Json;

namespace VanBaz;

public class BaseEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Новая база";
    public string Group { get; set; } = "";
    public string Kind { get; set; } = "File";
    public string Location { get; set; } = "";
    public string Version { get; set; } = "8.3";
    public string Executable { get; set; } = "";
    public string Notes { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Client { get; set; } = "Авто";
    public bool Favorite { get; set; }
    public DateTime? LastLaunch { get; set; }
    public decimal Order { get; set; }
    public string Architecture { get; set; } = "Авто";
    public string Description { get; set; } = "";
    public string ConfigurationVersion { get; set; } = "";
    public string ConfigurationName { get; set; } = "";
    public string ConfigurationId { get; set; } = "";
    public string ExtraArguments { get; set; } = "";
    public AdvancedLaunch Advanced { get; set; } = new();
    public string ExternalProcessor { get; set; } = "";
    public bool RunAsAdmin { get; set; }
    public bool DisallowDelete { get; set; }
    public bool EnterpriseLogin { get; set; } = true;
    public bool DesignerLogin { get; set; } = true;
    public string DesignerUser { get; set; } = "";
    public string EnterpriseSecret { get; set; } = "";
    public string DesignerSecret { get; set; } = "";
    public bool EnterprisePasswordMissing { get; set; }
    public bool DesignerPasswordMissing { get; set; }
    public string WindowsAuthentication { get; set; } = "Авто";
    public bool UseRepository { get; set; }
    public string Repository { get; set; } = "";
    public string RepositoryUser { get; set; } = "";
    public string RepositorySecret { get; set; } = "";
    public bool RepositoryPasswordMissing { get; set; }
    public string DoubleClickAction { get; set; } = "Предприятие";
    public string UpdateInfoUrl { get; set; } = "";
    public string LatestRelease { get; set; } = "";
    public DateTime? ReleaseChecked { get; set; }
    public Dictionary<string, string> V8iFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ListSources { get; set; } = new();
    public Dictionary<string, string> SourceSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonIgnore] public string ConnectionDisplay => Kind switch
    {
        "File" => "File=\"" + Location.Replace("\"", "\"\"") + "\";", "Server" => ServerConnection(),
        _ => Location
    };
    private string ServerConnection()
    {
        int slash = Location.IndexOf('/');
        return slash > 0 ? "Srvr=\"" + Location[..slash].Replace("\"", "\"\"") + "\";Ref=\"" + Location[(slash + 1)..].Replace("\"", "\"\"") + "\";" : Location;
    }
    public BaseEntry Copy() => JsonSerializer.Deserialize<BaseEntry>(JsonSerializer.Serialize(this))!;
}

public class LaunchRecord
{
    public string BaseId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "";
    public DateTime Time { get; set; }
}

public class Catalog
{
    public OfficialListState OfficialLists { get; set; } = new();
    public SnapshotInfo? Snapshot { get; set; }
    public int SchemaVersion { get; set; } = 3;
    public List<BaseEntry> Bases { get; set; } = new();
    public List<LaunchRecord> History { get; set; } = new();
    public List<BaseGroup> Groups { get; set; } = new();
    public List<CommandPreset> Commands { get; set; } = new();
    public Preferences Settings { get; set; } = new();
    public List<ListSyncState> ListSync { get; set; } = new();
    public List<LaunchProfile> LaunchProfiles { get; set; } = new();
    public MigrationReport? Migration { get; set; }
    public List<ConfigurationSource> Configurations { get; set; } = new();
    public int FeatureMigrationVersion { get; set; }
    public Catalog Copy() => JsonSerializer.Deserialize<Catalog>(JsonSerializer.Serialize(this))!;
}

public class BaseGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Path { get; set; } = "";
    public decimal Order { get; set; }
    public string Color { get; set; } = "#287EB0";
    public Dictionary<string, string> V8iFields { get; set; } = new();
    public List<string> ListSources { get; set; } = new();
}

public class CommandPreset
{
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Kind { get; set; } = "Параметры 1С";
    public string Target { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    [JsonIgnore] public string Description => Kind == "Параметры 1С" ? Arguments : Target;
}

public class Preferences
{
    public Dictionary<int, string> Bookmarks { get; set; } = new();
    public string ProxyMode { get; set; } = "Авто";
    public string ProxyUser { get; set; } = "";
    public string ProxySecret { get; set; } = "";
    public bool MissingBold { get; set; }
    public bool MissingItalic { get; set; }
    public bool MissingStrike { get; set; }
    public bool CellHighlight { get; set; }
    public bool PromptScreenshot { get; set; } = true;
    public string ScreenshotMonitor { get; set; } = "Все мониторы";
    public string StartupMonitor { get; set; } = "Автоопределение";
    public bool Maximized { get; set; }
    public List<string> PlatformRoots { get; set; } = new();
    public List<string> DisabledPlatformRoots { get; set; } = new();
    public string WebBrowser { get; set; } = "Edge";
    public Dictionary<string, string> BaseKeys { get; set; } = new();
    public string AppUpdateUrl { get; set; } = "";
    public bool CheckAppUpdates { get; set; }
    public Dictionary<string, double> ColumnWidths { get; set; } = new();
    public string SortColumn { get; set; } = "";
    public bool SortDescending { get; set; }
    public List<ListSource> PlatformLists { get; set; } = new();
    public string FontName { get; set; } = "Tahoma";
    public double FontSize { get; set; } = 11;
    public double RowHeight { get; set; } = 22;
    public bool Alphabetical { get; set; } = true;
    public bool GroupsFirst { get; set; } = true;
    public bool ShowCounts { get; set; }
    public bool ShowLaunchButtonInRow { get; set; }
    public bool EnableLegacy77 { get; set; }
    public bool ShowHistory { get; set; } = true;
    public bool ShowCommands { get; set; } = true;
    public bool ShowStarter { get; set; } = true;
    public bool ShowPlatform { get; set; } = true;
    public bool ShowDate { get; set; } = true;
    public bool ShowSize { get; set; } = true;
    public bool CheckExistence { get; set; } = true;
    public bool UseListColors { get; set; } = true;
    public bool UseFavoriteColor { get; set; } = true;
    public List<string> DisabledPlatformColors { get; set; } = new();
    public string FavoriteColor { get; set; } = "#8A0D06";
    public string MissingColor { get; set; } = "#6B6B6B";
    public SavedWindowPlacement? WindowPlacement { get; set; }
    public bool RestorePosition { get; set; }
    public double Width { get; set; } = 735;
    public double Height { get; set; } = 630;
    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
    public bool CloseToTray { get; set; }
    public bool EscapeToTray { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool MinimizeAfterLaunch { get; set; }
    public string AfterLaunchAction { get; set; } = "Свернуть в трей";
    public bool ListTooltips { get; set; } = true;
    public bool ShowColumnHeaders { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool NewBaseNameFromFolder { get; set; } = true;
    public bool NewBaseNameWithDate { get; set; }
    public string DefaultDoubleClickAction { get; set; } = "Предприятие";
    public string DefaultVersion { get; set; } = "8.3";
    public string DefaultFolder { get; set; } = "";
    public string SelectedId { get; set; } = "";
    public string Theme { get; set; } = "Светло-серый";
    public int SelectionIntensity { get; set; } = 128;
    public List<ThemeDefinition> Themes { get; set; } = new();
    public List<string> FavoriteThemes { get; set; } = new() { "Светло-серый", "Тёмный", "Голубой", "Песочный", "Зелёный" };
    public string InterfaceMode { get; set; } = "Разработчик";
    public string ProxyUrl { get; set; } = "";
    public bool ProxyWindowsCredentials { get; set; }
    public bool AutoSnapshot { get; set; } = true;
    public bool HotkeysEnabled { get; set; }
    public string ShowHotkey { get; set; } = "Ctrl+Alt+S";
    public string TrayHotkey { get; set; } = "Ctrl+Alt+Z";
    public string ScreenshotHotkey { get; set; } = "Ctrl+Shift+F12";
    public string ScreenshotFolder { get; set; } = "";
    public List<string> AlternativeLists { get; set; } = new();
    public Dictionary<string, string> PlatformColors { get; set; } = new();
    public List<string> CollapsedGroups { get; set; } = new();
    public Dictionary<string, string> SourceSettings { get; set; } = new();
}

public class MigrationReport
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string SettingsFolder { get; set; } = "";
    public string ListFile { get; set; } = "";
    public int SourceBases { get; set; }
    public int SourceGroups { get; set; }
    public int MatchedSettings { get; set; }
    public int Added { get; set; }
    public int Existing { get; set; }
    public int PasswordFields { get; set; }
    public int OrphanSettings { get; set; }
    public List<string> Notes { get; set; } = new();
    public string Summary => $"Баз в источнике: {SourceBases}; групп: {SourceGroups}\nДобавлено: {Added}; уже были в каталоге: {Existing}\nСовпадений настроек по ID: {MatchedSettings}\nСтарых записей настроек без подключения: {OrphanSettings}\n\n" + string.Join("\n", Notes);
}

public class ConfigurationSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Country { get; set; } = "Россия";
    public string ShortName { get; set; } = "";
    public string Edition { get; set; } = "";
    public string Platform { get; set; } = "83";
    public string InfoUrl { get; set; } = "";
    public string DownloadRoot { get; set; } = "";
    public bool Watch { get; set; } = true;
    public string LatestVersion { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public DateTime? Checked { get; set; }
    public string Error { get; set; } = "";
    [JsonIgnore] public string Status => Error.Length > 0 ? Error : Checked?.ToString("dd.MM.yyyy HH:mm") ?? "Не проверено";
    public ConfigurationSource Copy() => JsonSerializer.Deserialize<ConfigurationSource>(JsonSerializer.Serialize(this))!;
}

public sealed record PlatformInfo(string Version, string Directory, string Architecture)
{
    public string Family => "1С:Предприятие " + string.Join(".",Version.Split('.').Take(2));
    public string Display => Version + "  ·  " + Architecture;
    // Snapshot from the background scan: WPF bindings must not probe network paths.
    public bool ThickClient { get; init; }
    public bool ThinClient { get; init; }
}

public class ListRow
{
    public BaseEntry? Entry { get; set; }
    public bool IsGroup { get; set; }
    public string GroupKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "▣";
    public System.Windows.Controls.Image DisclosureImage => UiIcons.Icon(Icon == "▸" ? "tree-closed" : "tree-open");
    public System.Windows.Media.ImageSource IconImage => UiIcons.Drawing(IsGroup ? GroupKey == "@favorites" ? "★" : Icon == "▸" ? "folder" : "folder-open" : Entry?.Kind == "Server" ? "server" : Entry?.Kind == "Web" ? "web" : "file");
    public bool MissingBold { get; set; }
    public bool MissingItalic { get; set; }
    public bool MissingStrike { get; set; }
    public string FavoriteGlyph => Entry?.Favorite == true ? "★" : "☆";
    public string Version => Entry?.Version ?? "";
    public string Date { get; set; } = "";
    public string Size { get; set; } = "";
    public string Hint { get; set; } = "";
    public string Foreground { get; set; } = "#141414";
    public int Indent { get; set; }
    public System.Windows.Thickness Indentation => new(Indent, 0, 0, 0);
}
