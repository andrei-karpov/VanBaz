using System.IO.Compression;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VanBaz;

internal static class V08Smoke
{
    public static async Task Run(string scratch, Action<bool, string> check)
    {
        var entry = new BaseEntry { Version = "7.7", Name = "База \"Тест\"", Location = @"D:\Базы\Тест" };
        string reg = LegacyListFile.Export(new[] { entry }); var imported = LegacyListFile.Parse(reg);
        bool deleteRejected = false; try { LegacyListFile.Parse(reg + "\"C:\\\\bad\"=-"); } catch (InvalidDataException) { deleteRejected = true; }
        var command = LaunchService.SplitArguments(DesktopIntegration.ExplorerCommand(@"C:\Program Files\VanBaz\VanBaz.exe", @"D:\Own Data", "--designer"));
        check(imported.Single().Name == entry.Name && imported[0].Location == entry.Location && imported[0].Id == LegacyListFile.Id(entry.Location) && deleteRejected && command.Contains("%1") && command.Contains("--designer") && command.Contains(@"D:\Own Data"), "7.7 REG round-trip is text-only, rejects deletion; Explorer arguments preserve spaces and selected mode");

        string root = Path.Combine(scratch, "legacy-indexes"); Directory.CreateDirectory(root); entry.Location = root;
        File.WriteAllText(Path.Combine(root, "1Cv7.MD"), "synthetic metadata"); File.WriteAllText(Path.Combine(root, "table.dbf"), "synthetic data"); File.WriteAllText(Path.Combine(root, "table.cdx"), "index");
        bool activeRejected = false; try { LegacyIndexService.Quarantine(entry, () => true); } catch (IOException) { activeRejected = true; }
        string quarantine = LegacyIndexService.Quarantine(entry, () => false);
        bool moved = !File.Exists(Path.Combine(root, "table.cdx")) && LegacyIndexService.Archives(entry).Single().Count == 1;
        File.WriteAllText(Path.Combine(root, "table.cdx"), "new index");
        bool collision = false; try { LegacyIndexService.Restore(entry, quarantine, () => false); } catch (IOException) { collision = true; }
        File.Delete(Path.Combine(root, "table.cdx")); LegacyIndexService.Restore(entry, quarantine, () => false);
        string profile = Path.Combine(scratch, "profile"); Directory.CreateDirectory(Path.Combine(profile, "AppData"));
        check(activeRejected && moved && collision && File.ReadAllText(Path.Combine(root, "table.cdx")) == "index" && File.ReadAllText(Path.Combine(root, "table.dbf")) == "synthetic data" && File.ReadAllText(Path.Combine(root, "1Cv7.MD")) == "synthetic metadata" && CacheService.ProfileRoots(profile).Length == 4 && LegacyIndexService.Archives(entry).Count == 0, "7.7 CDX quarantine/restoration preserves database files, refuses clients and new indexes; profile roots stay within chosen AppData");

        string app = Environment.GetEnvironmentVariable("VANBAZ_APP_FOLDER") ?? throw new Exception("Set VANBAZ_APP_FOLDER");
        var manifest = new AppPackage { Version = System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(app, "VanBaz.dll")).Version!.ToString(3) };
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            foreach (string file in AppUpdateService.Files)
            {
                byte[] content = File.ReadAllBytes(Path.Combine(app, file)); manifest.Files[file] = Convert.ToHexString(SHA256.HashData(content));
                using var output = zip.CreateEntry(file).Open(); output.Write(content);
            }
            using var header = new StreamWriter(zip.CreateEntry("vanbaz-package.json").Open()); header.Write(JsonSerializer.Serialize(manifest));
        }
        byte[] package = bytes.ToArray(); const string url = "https://updates.example.invalid/channel.json";
        var release = new AppRelease { Version = manifest.Version, PackageUrl = "VanBaz.zip", Size = package.Length, Sha256 = Convert.ToHexString(SHA256.HashData(package)), Notes = "Synthetic release" };
        using var transport = new HttpClient(new Transport(JsonSerializer.SerializeToUtf8Bytes(release), package));
        var found = await AppUpdateFeed.Check(url, new(), CancellationToken.None, transport);
        var prepared = await AppUpdateFeed.Download(found, url, new(), Path.Combine(scratch, "feed"), null, CancellationToken.None, transport);
        found.Sha256 = new string('0', 64); bool hashRejected = false;
        try { await AppUpdateFeed.Download(found, url, new(), Path.Combine(scratch, "badfeed"), null, CancellationToken.None, transport); } catch (IOException) { hashRejected = true; }
        bool otherHostRejected = false; release.PackageUrl = "https://another.invalid/app.zip";
        try { AppUpdateFeed.Parse(JsonSerializer.Serialize(release), new Uri(url)); } catch (InvalidDataException) { otherHostRejected = true; }
        check(prepared.Version == manifest.Version && hashRejected && otherHostRejected && !Directory.GetFiles(Path.Combine(scratch, "badfeed"), "*.zip").Any(), "HTTPS update transport validates feed, streams package, checks hashes and version, rejects cross-host source, cleans failed download; no network or installation");
    }
    private sealed class Transport(byte[] feed, byte[] package) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith(".json") ? feed : package) });
        }
    }
}
