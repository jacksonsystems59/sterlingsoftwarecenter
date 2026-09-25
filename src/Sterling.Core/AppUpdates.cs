using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Sterling.Core;
public record AppRelease(string Version, string Page, string ZipUrl, string ChecksumUrl, string Filename, string Notes = "");
public sealed class AppUpdates
{
    public const string Repository = "jacksonsystems59/sterlingsoftwarecenter";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    static AppUpdates() { Http.DefaultRequestHeaders.UserAgent.ParseAdd("SterlingSoftwareCentre/0.1.1"); }
    public async Task<AppRelease?> Check(Version current)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString()!;
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..], out var version) || version <= new Version(current.Major, current.Minor, current.Build)) return null;
        string filename = $"SterlingSoftwareCentre-{tag}-win-x64.zip";
        string Asset(string name) => root.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == name).GetProperty("browser_download_url").GetString()!;
        string zip = Asset(filename), checksum = Asset(filename + ".sha256");
        ValidateAsset(zip, tag, filename); ValidateAsset(checksum, tag, filename + ".sha256");
        return new(tag[1..], $"https://github.com/{Repository}/releases/tag/{tag}", zip, checksum, filename, root.TryGetProperty("body", out var body) ? body.GetString() ?? "No release notes provided." : "No release notes provided.");
    }
    static void ValidateAsset(string url, string tag, string name)
    {
        if (url != $"https://github.com/{Repository}/releases/download/{tag}/{name}") throw new InvalidDataException("Unexpected release asset location.");
    }
    public static string ExpectedHash(string text, string filename)
    {
        var match = Regex.Match(text.Trim(), @"^([a-fA-F0-9]{64})\s+\*?([^\r\n]+)$");
        if (!match.Success || match.Groups[2].Value != filename) throw new InvalidDataException("Invalid release checksum file.");
        return match.Groups[1].Value;
    }
    public static async Task Verify(string path, string expected)
    {
        await using var input = File.OpenRead(path);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(input).ConfigureAwait(false));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 mismatch. The update will not be installed.");
    }
    public async Task<string> Stage(AppRelease release, Action<string> log)
    {
        string directory = Path.Combine(Storage.Home, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string zip = Path.Combine(directory, release.Filename);
        log("Downloading " + release.Filename);
        using (var response = await Http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(zip);
            await response.Content.CopyToAsync(file);
        }
        await Verify(zip, ExpectedHash(await Http.GetStringAsync(release.ChecksumUrl), release.Filename));
        string stage = Path.Combine(directory, "stage");
        ExtractSafe(zip, stage);
        if (!File.Exists(Path.Combine(stage, "Sterling.App.exe")) || !File.Exists(Path.Combine(stage, "Sterling.Updater.exe")) || !File.Exists(Path.Combine(stage, "sterling-portable.json"))) throw new InvalidDataException("Release package is incomplete.");
        log("Release checksum verified. Update ready.");
        return stage;
    }
    public static void ExtractSafe(string zip, string destination)
    {
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Sum(e => e.Length) > 2_000_000_000 || archive.Entries.Count > 5000) throw new InvalidDataException("Release archive exceeds limits.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var path = SafePath(destination, entry.FullName);
            if (!names.Add(path) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new InvalidDataException("Duplicate or symbolic-link archive entry.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, false);
        }
    }
    public static string SafePath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(p => p is ".." or "." || p.EndsWith(' ') || p.EndsWith('.'))) throw new InvalidDataException("Unsafe update path.");
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path escapes update folder.");
        return path;
    }
    public static void RejectReparseAncestors(string path)
    {
        for (var dir = new DirectoryInfo(path); dir != null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Updating inside a junction/symlink is not supported. Extract to a normal local folder.");
    }
    public static void Apply(string stage, string target, string backup, Action<int>? faultForTest = null)
    {
        RejectReparseAncestors(target); RejectReparseAncestors(stage);
        if (!File.Exists(Path.Combine(target, "sterling-portable.json"))) throw new InvalidDataException("Target is not a Sterling portable application folder.");
        var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
        Directory.CreateDirectory(backup);
        var changed = new List<(string Target, string? Original)>();
        try
        {
            foreach (var file in files)
            {
                string relative = Path.GetRelativePath(stage, file);
                string dest = SafePath(target, relative), saved = SafePath(backup, relative);
                RejectReparseAncestors(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest) && (File.GetAttributes(dest) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Target file is a link.");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                string? original = null;
                if (File.Exists(dest)) { Directory.CreateDirectory(Path.GetDirectoryName(saved)!); File.Copy(dest, saved, false); original = saved; }
                changed.Add((dest, original));
                File.Copy(file, dest, true);
                faultForTest?.Invoke(changed.Count);
            }
        }
        catch
        {
            List<Exception> failures = [];
            foreach (var (dest, original) in changed.AsEnumerable().Reverse())
                try { if (original != null) File.Copy(original, dest, true); else File.Delete(dest); } catch (Exception ex) { failures.Add(ex); }
            if (failures.Count > 0) throw new IOException("Update failed and recovery was incomplete. Restore files from " + backup, new AggregateException(failures));
            throw;
        }
    }
}
