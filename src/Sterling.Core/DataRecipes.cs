using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Sterling.Core;

public sealed class DataOptions
{
    public bool RestoreBookmarks { get; set; }
    public string ChromeProfile { get; set; } = "Default";
    public string BookmarksFile { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
public interface IDataRecipeRunner
{
    string Describe(Package package);
    Task<string> Restore(Package package);
}
public sealed class BookmarkRecipe(string userDataRoot, Func<bool> chromeRunning) : IDataRecipeRunner
{
    public static bool Supports(Package p) => (p.Provider == "winget" && p.Id == "Google.Chrome") || (p.Provider == "chocolatey" && p.Id.Equals("googlechrome", StringComparison.OrdinalIgnoreCase));
    public string Target(DataOptions options)
    {
        if (!Regex.IsMatch(options.ChromeProfile, @"^(Default|Profile [0-9]+)$")) throw new InvalidDataException("Choose a supported Chrome profile: Default or Profile N.");
        string path = AppUpdates.SafePath(userDataRoot, Path.Combine(options.ChromeProfile, "Bookmarks"));
        AppUpdates.RejectReparseAncestors(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Bookmark links are not supported.");
        return path;
    }
    public string Describe(Package p) => $"Chrome bookmarks · replace in {Target(p.Data)} · current user {Environment.UserName}. Backup: {p.Data.BookmarksFile}. Existing bookmarks are saved beside the target; no passwords, cookies, registry or shortcuts. Close Chrome first; Sync may reconcile changes.";
    public static void ValidateFile(string path)
    {
        if (new FileInfo(path).Length > 50_000_000) throw new InvalidDataException("Bookmarks file exceeds 50 MB.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("roots", out var roots) || !roots.TryGetProperty("bookmark_bar", out _)) throw new InvalidDataException("Not a Chrome bookmarks file.");
    }
    public async Task<string> Restore(Package package)
    {
        if (!Supports(package) || !package.Data.RestoreBookmarks) throw new InvalidDataException("This application has no selected supported data recipe.");
        if (chromeRunning()) throw new InvalidOperationException("Close Chrome, including background processes, before restoring bookmarks.");
        string target = Target(package.Data);
        ValidateFile(package.Data.BookmarksFile);
        if (package.Data.Sha256.Length != 64) throw new InvalidDataException("Choose a backup file so its checksum can be recorded before restoring.");
        await AppUpdates.Verify(package.Data.BookmarksFile, package.Data.Sha256);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string backup = target + ".sterling-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".bak";
        if (File.Exists(target)) File.Copy(target, backup, false);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.Copy(package.Data.BookmarksFile, temp, false);
        File.Move(temp, target, true);
        await AppUpdates.Verify(target, package.Data.Sha256);
        return "Bookmarks restored for " + Environment.UserName + ". Previous file (if present): " + backup;
    }
    public void Backup(DataOptions options, string destination)
    {
        if (chromeRunning()) throw new InvalidOperationException("Close Chrome, including background processes, before backing up bookmarks.");
        string source = Target(options); ValidateFile(source); File.Copy(source, destination, true);
        options.BookmarksFile = Path.GetFullPath(destination);
        options.Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination)));
    }
}
