using System.Text.Json;
using VanBaz;

internal static class V14Smoke
{
    public static void Run(Action<bool,string> check)
    {
        const string old = """
        {"FeatureMigrationVersion":5,"Bases":[{"Id":"kept","Name":"Kept","Location":"D:\\Fixture","Favorite":true,"BackupFolder":"D:\\OldCopies","BackupProfileId":"old","SourceSettings":{"BackupScriptID":"old","Description":"keep"}}],"BackupProfiles":[{"Id":"old"}],"Backups":[{"BaseId":"kept"}],"Settings":{"FontName":"Calibri","FontSize":14,"RowHeight":29,"BackupFolder":"D:\\OldCopies","Archiver7z":"old.exe"}}
        """;
        var catalog=JsonSerializer.Deserialize<Catalog>(old)!;
        FeatureMigration.Apply(catalog); string once=JsonSerializer.Serialize(catalog); FeatureMigration.Apply(catalog);
        check(catalog.Bases.Single().Id=="kept" && catalog.Bases[0].Favorite && catalog.Bases[0].SourceSettings["Description"]=="keep" && !once.Contains("Backup") && !once.Contains("Archiver") && once==JsonSerializer.Serialize(catalog), "v14 legacy JSON keeps registrations/private state, drops retired backup settings and migrates idempotently");
        check(catalog.Settings.FontName=="Tahoma" && catalog.Settings.FontSize==11 && catalog.Settings.RowHeight==22, "v14 untouched legacy UI defaults migrate to compact dimensions");
        catalog.FeatureMigrationVersion=5; catalog.Settings.FontName="Calibri"; catalog.Settings.FontSize=14; catalog.Settings.RowHeight=29; catalog.Settings.SourceSettings["FontName"]="Calibri"; catalog.Settings.SourceSettings["DefNodeHeight"]="29";
        FeatureMigration.Apply(catalog);
        check(catalog.Settings.FontName=="Calibri" && catalog.Settings.RowHeight==29, "v14 explicit imported font and row sizes remain user-controlled");
    }
}
