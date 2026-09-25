using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Sterling.Core;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class Package : Observable
{
    bool selected;
    public bool Selected { get => selected; set { selected = value; Changed(); } }
    public string Provider { get; set; } = "winget";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Available { get; set; } = "";
    public string Scope { get; set; } = "unknown";
    public string Match { get; set; } = "Provider match";
    public string Disposition { get; set; } = "Ready for reinstall";
    public string VersionPolicy { get; set; } = "Newest";
    public bool Excluded { get; set; }
    public bool Pinned { get; set; }
    public bool MultipleProviders { get; set; }
    [JsonIgnore] public string Key => Provider + ":" + Id + ":" + Scope;
    [JsonIgnore] public bool Manageable => Match == "Provider match" && Rules.ValidId(Id) && Provider is "winget" or "chocolatey";
    [JsonIgnore] public bool Eligible => Manageable && !Excluded && !Pinned && !MultipleProviders && !string.IsNullOrWhiteSpace(Available) && Rules.KnownVersion(Version);
    [JsonIgnore] public string State => Excluded ? "Excluded" : MultipleProviders ? "Multiple providers — review" : Pinned ? "Pinned / pin status unavailable" : Match;
    public Package Copy() => JsonSerializer.Deserialize<Package>(JsonSerializer.Serialize(this))!;
}

public sealed class Bundle
{
    public int SchemaVersion { get; set; } = 1;
    public string Kind { get; set; } = "capture";
    public string Name { get; set; } = "Captured PC";
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Machine { get; set; } = Environment.MachineName;
    public List<Package> Packages { get; set; } = [];
}

public sealed class Settings
{
    public string ChocolateySource { get; set; } = "";
    public bool AcceptSourceAgreements { get; set; }
    public bool CheckAppUpdates { get; set; } = true;
    public HashSet<string> Exclusions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static partial class Rules
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,199}$")] private static partial Regex IdPattern();
    public static bool ValidId(string? value) => value != null && IdPattern().IsMatch(value);
    public static bool KnownVersion(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) && value != "N/A" && !value.Contains('…') && !value.StartsWith('<') && !value.StartsWith('>');
    public static void Validate(Package p, bool requireVersion = true)
    {
        if (!p.Manageable) throw new InvalidDataException("Select an exact provider package; this item requires manual review.");
        if (p.Scope is not ("unknown" or "user" or "machine")) throw new InvalidDataException("Scope must be unknown, user or machine.");
        if (p.VersionPolicy is not ("Newest" or "Captured")) throw new InvalidDataException("Version policy must be Newest or Captured.");
        if (requireVersion && p.VersionPolicy == "Captured" && (!KnownVersion(p.Version) || p.Version.Length > 100 || p.Version.Any(char.IsControl) || p.Version.StartsWith('-')))
            throw new InvalidDataException("The captured version is not a usable exact version.");
    }
    public static string ValidateSource(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Configure an HTTPS internal/customer Chocolatey repository without embedded credentials or query strings.");
        if (uri.Host.Equals("chocolatey.org", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".chocolatey.org", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Direct Community Repository use is not supported. Configure an authorised internal repository or compliant proxy.");
        return uri.AbsoluteUri;
    }
}

public static class Storage
{
    public static string Home => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sterling", "SoftwareCentre");
    public static JsonSerializerOptions Json { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, path, true);
    }
    public static T Load<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty document.");
    public static Bundle LoadBundle(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Bundle exceeds 16 MB.");
        var b = Load<Bundle>(path);
        if (b.SchemaVersion != 1 || b.Kind is not ("capture" or "list") || b.Packages == null || b.Packages.Count > 10000)
            throw new InvalidDataException("Unsupported bundle format/version.");
        foreach (var p in b.Packages)
        {
            if (p == null) throw new InvalidDataException("Invalid package entry.");
            p.Selected = false;
            if (p.Manageable) { Rules.Validate(p, false); if (p.VersionPolicy == "Captured" && !Rules.KnownVersion(p.Version)) p.Disposition = "Captured version unknown — review policy"; }
            else { p.Match = "Unknown — manual review"; p.Disposition = "Manual attention / custom installer"; }
        }
        return b;
    }
}
