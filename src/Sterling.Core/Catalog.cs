using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Sterling.Core;

public sealed class CommonApplication : Observable
{
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "machine";
    public bool Enabled { get; set; } = true;
    public string Reason { get; set; } = "";
    public bool Selected { get; set; }
    public string Installed { get; set; } = "Not scanned";
    public string Manifest { get; set; } = "";
    public Package Package() => new() { Id = Id, Name = Name, Scope = Scope, VersionPolicy = "Newest" };
}
public record PackageDetails(string Id, string Source, string Version, string Publisher, string Description, string Link, string InstallerType, string ReleaseNotes, string ReleaseDate, DateTimeOffset CheckedAt);
public sealed class PackageInformation(Settings settings, ICommandRunner runner)
{
    readonly Dictionary<string, PackageDetails> cache = [];
    public async Task<PackageDetails> Details(Package p, bool refresh = false)
    {
        Rules.Validate(p, false);
        string key = p.Provider + ":" + p.Id + ":" + p.Scope + ":" + (p.VersionPolicy == "Captured" ? p.Version : "latest");
        if (!refresh && cache.TryGetValue(key, out var cached) && cached.CheckedAt > DateTimeOffset.UtcNow.AddHours(-6)) return cached;
        if (p.Provider != "winget") return new(p.Id, settings.ChocolateySource, p.Available, "Unavailable", "Use your internal package documentation. Publisher metadata is unavailable.", "", "unverified", "", "", DateTimeOffset.UtcNow);
        List<string> args = ["show", "--id", p.Id, "--exact", "--source", "winget", "--disable-interactivity"];
        args.AddRange(["--architecture", "x64"]); if (p.Scope != "unknown") args.AddRange(["--scope", p.Scope]);
        if (settings.AcceptSourceAgreements) args.Add("--accept-source-agreements");
        if (p.VersionPolicy == "Captured") args.AddRange(["--version", p.Version]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var r = await runner.Run(new(WingetProvider.Executable, args), token: timeout.Token);
        if (unchecked((uint)r.Code) == 0x8A150014) throw new KeyNotFoundException("Exact package/version is not present in the source.");
        r.EnsureSuccess();
        string Field(string name) { var m = Regex.Match(r.Output, @"(?m)^\s*" + Regex.Escape(name) + @":\s*(.*)$"); return m.Success ? m.Groups[1].Value.Trim() : ""; }
        string notes = Regex.Match(r.Output, @"(?ms)^Release Notes:\s*\r?\n(.*?)(?=^[A-Z][^\r\n]*:|\z)").Groups[1].Value.Trim();
        string link = Field("Release Notes Url"); if (!SafeLink(link)) link = Field("Homepage"); if (!SafeLink(link)) link = Field("Publisher Url");
        var result = new PackageDetails(p.Id, "WinGet community manifest (winget)", Field("Version"), Field("Publisher"), Field("Description"), SafeLink(link) ? link : "", Field("Installer Type"), notes, Field("Release Date"), DateTimeOffset.UtcNow);
        cache[key] = result; return result;
    }
    public static bool SafeLink(string s) => Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme == "https" && string.IsNullOrEmpty(u.UserInfo);
    public static bool SilentSupported(PackageDetails d) => d.InstallerType is "msi" or "wix" or "nullsoft" or "inno" or "burn" or "msix";
    public static string Categorize(Package p, PackageDetails? metadata, bool available, IReadOnlyList<CommonApplication> known)
    {
        if (!p.Manageable || p.MultipleProviders) return "Ambiguous/provider match unavailable — identify the exact package manually.";
        if (p.Excluded) return "Policy exclusion — review the customer's software policy.";
        if (p.Scope is not ("user" or "machine")) return "Scope unknown — choose user or machine and check again.";
        if (!available) return "Requested package/version unavailable — choose newest or obtain the vendor installer.";
        if (p.Provider == "chocolatey") return "Internal package unattended behaviour unverified — review with the repository administrator.";
        if (metadata == null || !(SilentSupported(metadata) || known.Any(a => a.Enabled && a.Id == p.Id && a.Scope == p.Scope && p.VersionPolicy == "Newest"))) return "Unattended method unverified — use the vendor procedure or verify the manifest.";
        return "Ready for automatic install";
    }
}
public record SecurityFinding(string PackageId, string Platform, string Edition, string LowerInclusive, string FixedVersion, string Cve, string Title, string Published, string Url)
{
    public bool Affects(Package p) => p.Provider == "winget" && p.Id == PackageId && Platform == "Windows" && VersionRange(p.Version, LowerInclusive, FixedVersion);
    public static bool VersionRange(string actual, string lower, string upper) => Version.TryParse(actual, out var a) && Version.TryParse(lower, out var l) && Version.TryParse(upper, out var u) && a >= l && a < u;
}
public sealed class SourceSnapshot
{
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? AttemptedAt { get; set; }
    public string Error { get; set; } = "";
    public Dictionary<string, PackageDetails> Packages { get; set; } = [];
    public List<string> Changes { get; set; } = [];
    public DateTimeOffset? Next(int hours) => CompletedAt?.AddHours(Math.Clamp(hours, 1, 168));
    public static List<string> Compare(Dictionary<string, PackageDetails> before, Dictionary<string, PackageDetails> after, HashSet<string> confirmedAbsent)
    {
        List<string> changes = [];
        foreach (var (id, p) in after)
            if (!before.TryGetValue(id, out var old)) changes.Add(id + ": newly observed in tracked sources.");
            else if (old.Publisher != p.Publisher || old.Source != p.Source) changes.Add(id + ": publisher/source metadata changed; this does not establish ownership transfer.");
        foreach (string id in confirmedAbsent.Where(before.ContainsKey)) changes.Add(id + ": no longer available in the checked source.");
        return changes;
    }
}
