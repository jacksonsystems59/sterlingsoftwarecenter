using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Sterling.Core;
public sealed class BrowserProfile
{
    public bool Selected { get; set; }
    public string Browser { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string User { get; set; } = "";
    public string Profile { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Bookmarks { get; set; } = true;
    public bool Settings { get; set; }
    public bool Extensions { get; set; }
    public string Status { get; set; } = "";
    public string Folder { get; set; } = "";
}
public sealed class BrowserManifest
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BrowserProfile> Profiles { get; set; } = [];
}
public static class BrowserBackups
{
    public static readonly (string Browser, string Id, string Relative, string Process)[] Definitions = [("Chrome", "Google.Chrome", @"AppData\Local\Google\Chrome\User Data", "chrome"), ("Edge", "Microsoft.Edge", @"AppData\Local\Microsoft\Edge\User Data", "msedge"), ("Brave", "Brave.Brave", @"AppData\Local\BraveSoftware\Brave-Browser\User Data", "brave"), ("Firefox", "Mozilla.Firefox", @"AppData\Roaming\Mozilla\Firefox\Profiles", "firefox")];
    public static List<BrowserProfile> Detect(string userRoot)
    {
        List<BrowserProfile> result = [];
        foreach (var d in Definitions)
        {
            string root = System.IO.Path.Combine(userRoot, d.Relative);
            if (!Directory.Exists(root)) continue;
            foreach (string dir in Directory.GetDirectories(root))
            {
                string name = System.IO.Path.GetFileName(dir);
                if (d.Browser != "Firefox" && name != "Default" && !System.Text.RegularExpressions.Regex.IsMatch(name, @"^Profile \d+$")) continue;
                if (d.Browser == "Firefox" && !File.Exists(System.IO.Path.Combine(dir, "prefs.js"))) continue;
                result.Add(new() { Browser = d.Browser, PackageId = d.Id, User = userRoot, Profile = name, Path = dir, Status = d.Browser == "Firefox" ? "Bookmarks: Firefox JSON backup, manual import. UI settings: xulstore.json. Extensions: information only." : "Bookmarks: automatic restore. Settings: bookmark bar/home button only. Extensions: information only." });
            }
        }
        return result;
    }
    public static void RequireClosed(string browser)
    {
        var d = Definitions.Single(x => x.Browser == browser);
        if (Process.GetProcessesByName(d.Process).Length > 0) throw new IOException("Close all " + browser + " processes, including background windows, before backup/restore.");
    }
    static void Copy(string source, string destination) { AppUpdates.RejectReparseAncestors(System.IO.Path.GetDirectoryName(source)!); if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Profile links are unsupported."); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!); File.Copy(source, destination, true); }
    public static async Task<BrowserManifest> Backup(IEnumerable<BrowserProfile> profiles, string zip, Action<string>? closedCheck = null)
    {
        string root = System.IO.Path.Combine(Storage.Home, "browser-jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var manifest = new BrowserManifest(); int i = 0;
        foreach (var original in profiles.Where(p => p.Selected))
        {
            var p = JsonSerializer.Deserialize<BrowserProfile>(JsonSerializer.Serialize(original))!; p.Folder = "profiles/" + i++; p.Selected = false;
            List<string> results = []; string target = AppUpdates.SafePath(root, p.Folder); Directory.CreateDirectory(target);
            try
            {
                (closedCheck ?? RequireClosed)(p.Browser);
                void Try(string type, Action action) { try { action(); results.Add(type + ": captured"); } catch (Exception ex) { results.Add(type + ": FAILED — " + ex.Message); } }
                if (p.Bookmarks) Try("Bookmarks", () =>
                {
                    if (p.Browser == "Firefox")
                    {
                        var backup = new DirectoryInfo(System.IO.Path.Combine(p.Path, "bookmarkbackups")).GetFiles("*.jsonlz4").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() ?? throw new IOException("Firefox has no bookmark snapshot yet. Create a bookmark backup in Firefox, then retry.");
                        Copy(backup.FullName, System.IO.Path.Combine(target, backup.Name)); results.Add("Firefox snapshot from " + backup.LastWriteTimeUtc.ToString("u") + "; restore through Firefox Bookmarks → Manage bookmarks → Import and Backup → Restore → Choose File.");
                    }
                    else { string src = System.IO.Path.Combine(p.Path, "Bookmarks"); BookmarkRecipe.ValidateFile(src); Copy(src, System.IO.Path.Combine(target, "Bookmarks")); }
                });
                if (p.Settings) Try("Settings", () =>
                {
                    if (p.Browser == "Firefox") Copy(System.IO.Path.Combine(p.Path, "xulstore.json"), System.IO.Path.Combine(target, "xulstore.json"));
                    else
                    {
                        var source = JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(p.Path, "Preferences")))!;
                        var selected = new JsonObject();
                        foreach (var (section, key) in new[] { ("bookmark_bar", "show_on_all_tabs"), ("browser", "show_home_button") })
                            if (source[section]?[key] is JsonValue v && v.TryGetValue<bool>(out bool value)) selected[section] = new JsonObject { [key] = value };
                        File.WriteAllText(System.IO.Path.Combine(target, "settings.json"), selected.ToJsonString());
                    }
                });
                if (p.Extensions) Try("Extension information", () =>
                {
                    var items = new List<object>();
                    if (p.Browser == "Firefox")
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(p.Path, "extensions.json")));
                        foreach (var a in doc.RootElement.GetProperty("addons").EnumerateArray()) items.Add(new { Id = a.GetProperty("id").GetString(), Version = a.GetProperty("version").GetString() });
                    }
                    else foreach (string dir in Directory.GetDirectories(System.IO.Path.Combine(p.Path, "Extensions"))) items.Add(new { Id = System.IO.Path.GetFileName(dir), Versions = Directory.GetDirectories(dir).Select(System.IO.Path.GetFileName).ToArray() });
                    Storage.Save(System.IO.Path.Combine(target, "extensions-information.json"), items);
                });
            }
            catch (Exception ex) { results.Add("FAILED — " + ex.Message); }
            p.Status = string.Join("; ", results); manifest.Profiles.Add(p);
        }
        if (manifest.Profiles.Count == 0) throw new InvalidOperationException("Select at least one detected browser profile.");
        Storage.Save(System.IO.Path.Combine(root, "manifest.json"), manifest);
        File.WriteAllText(System.IO.Path.Combine(root, "Report.txt"), "Passwords, cookies, sessions, saved authentication and extension binaries are excluded. Extension information does not reinstall extensions.\n" + string.Join("\n", manifest.Profiles.Select(p => p.User + " / " + p.Browser + " / " + p.Profile + ": " + p.Status)));
        await Task.Run(() => IntegrityBundle.Pack(root, zip)); return manifest;
    }
    public static async Task<string> Restore(string root, BrowserProfile source, BrowserProfile target, bool bookmarks, bool settings, Action<string>? closedCheck = null)
    {
        if (source.Browser != target.Browser) throw new InvalidDataException("Choose a target profile for the same browser.");
        await IntegrityBundle.VerifyDirectory(root);
        (closedCheck ?? RequireClosed)(target.Browser); AppUpdates.RejectReparseAncestors(target.Path);
        if (!Directory.Exists(target.Path)) throw new IOException("Open the installed browser once as the intended Windows user to create its profile, then close it and detect profiles again.");
        string folder = AppUpdates.SafePath(root, source.Folder); List<string> results = [];
        void Replace(string file, string data)
        {
            string dest = System.IO.Path.Combine(target.Path, file);
            if (File.Exists(dest)) File.Copy(dest, dest + ".sterling-" + Guid.NewGuid().ToString("N") + ".bak");
            string temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllText(temp, data); File.Move(temp, dest, true);
            if (File.ReadAllText(dest) != data) throw new IOException("Restored file verification failed.");
        }
        if (bookmarks)
        {
            if (source.Browser == "Firefox") results.Add("MANUAL: open Firefox's bookmark restore chooser and select the .jsonlz4 file in " + folder + ". Existing bookmarks will be replaced by Firefox.");
            else { string src = System.IO.Path.Combine(folder, "Bookmarks"); BookmarkRecipe.ValidateFile(src); Replace("Bookmarks", await File.ReadAllTextAsync(src)); results.Add("Bookmarks restored and file verified; previous target retained."); }
        }
        if (settings)
        {
            if (source.Browser == "Firefox") { string text = await File.ReadAllTextAsync(System.IO.Path.Combine(folder, "xulstore.json")); using var valid = JsonDocument.Parse(text); Replace("xulstore.json", text); }
            else
            {
                string targetFile = System.IO.Path.Combine(target.Path, "Preferences");
                var node = JsonNode.Parse(await File.ReadAllTextAsync(targetFile)) ?? throw new InvalidDataException("Invalid target preferences.");
                var sourceNode = JsonNode.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(folder, "settings.json")))!;
                foreach (var (section, key) in new[] { ("bookmark_bar", "show_on_all_tabs"), ("browser", "show_home_button") })
                    if (sourceNode[section]?[key] is JsonValue v && v.TryGetValue<bool>(out bool value)) { node[section] ??= new JsonObject(); node[section]![key] = value; }
                Replace("Preferences", node.ToJsonString());
            }
            results.Add("Supported settings restored; previous target retained.");
        }
        results.Add("Extension information is a reference for manual store installation. No secrets or signed-in sessions restored.");
        return string.Join("\n", results);
    }
}
