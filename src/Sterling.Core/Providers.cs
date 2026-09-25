namespace Sterling.Core;

public interface IPackageProvider
{
    string Name { get; }
    Task<string> Detect();
    Task<List<Package>> Search(string query);
    Task<List<Package>> Inventory();
    Task<bool> Available(Package package);
    Task<ProcessResult> Execute(Package package, string operation, Action<string> log);
}

public sealed class WingetProvider(ICommandRunner runner, Settings settings) : IPackageProvider
{
    public string Name => "winget";
    public static string Executable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
    public Command Build(string operation, Package p)
    {
        if (operation is not ("install" or "upgrade" or "uninstall")) throw new InvalidDataException("Unsupported operation.");
        Rules.Validate(p);
        List<string> args = [operation, "--id", p.Id, "--exact", "--source", "winget", "--disable-interactivity"];
        if (settings.AcceptSourceAgreements) args.Add("--accept-source-agreements");
        if (p.Scope != "unknown") args.AddRange(["--scope", p.Scope]);
        if (operation != "uninstall")
        {
            args.AddRange(["--accept-package-agreements", "--silent"]);
            if (operation == "install") args.Add("--no-upgrade");
            if (p.VersionPolicy == "Captured") args.AddRange(["--version", p.Version]);
        }
        return new(Executable, args);
    }
    async Task<ProcessResult> Query(params string[] args)
    {
        List<string> list = [..args, "--disable-interactivity"];
        if (settings.AcceptSourceAgreements) list.Add("--accept-source-agreements");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        return await runner.Run(new(Executable, list), token: timeout.Token);
    }
    public async Task<string> Detect() { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var r = await runner.Run(new(Executable, ["--version"]), token: timeout.Token); r.EnsureSuccess(); return r.Output.Trim(); }
    public async Task<List<Package>> Search(string query)
    {
        var r = await Query("search", "--query", query, "--source", "winget");
        if (unchecked((uint)r.Code) == 0x8A150014) return [];
        r.EnsureSuccess();
        return WingetTable.Parse(r.Output).Select(row => FromRow(row, "unknown")).ToList();
    }
    static Package FromRow(Dictionary<string, string> row, string scope) => new()
    {
        Id = row["Id"], Name = row["Name"], Version = row.GetValueOrDefault("Version", ""), Available = row.GetValueOrDefault("Available", ""), Scope = scope
    };
    public async Task<List<Package>> Inventory()
    {
        List<Package> packages = [];
        // Scope is detected through separate provider queries, never inferred from display names.
        foreach (var scope in new[] { "user", "machine" })
        {
            var r = await Query("list", "--source", "winget", "--scope", scope);
            if (unchecked((uint)r.Code) == 0x8A150014) continue;
            r.EnsureSuccess();
            packages.AddRange(WingetTable.Parse(r.Output).Select(row => FromRow(row, scope)));
        }
        bool pinsKnown = false;
        HashSet<string> pins = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            // pin list has no source-agreement switch.
            var r = await runner.Run(new(Executable, ["pin", "list", "--disable-interactivity"]));
            if (r.Success && r.Output.Contains("There are no pins configured.", StringComparison.Ordinal)) pinsKnown = true;
            else if (r.Success) { foreach (var row in WingetTable.Parse(r.Output)) pins.Add(row["Id"]); pinsKnown = true; }
        }
        catch { /* Fail closed: no bulk updates when pin enumeration is unavailable. */ }
        foreach (var p in packages) p.Pinned = !pinsKnown || pins.Contains(p.Id);
        // Multiple instances of the same ID/scope cannot be addressed individually by CLI.
        foreach (var group in packages.GroupBy(p => p.Key).Where(g => g.Count() > 1))
            foreach (var p in group) { p.Match = "Multiple instances — manual review"; p.Disposition = "Manual attention"; }
        return packages;
    }
    public async Task<bool> Available(Package p)
    {
        Rules.Validate(p);
        List<string> args = ["show", "--id", p.Id, "--exact", "--source", "winget"];
        if (p.VersionPolicy == "Captured") args.AddRange(["--version", p.Version]);
        return (await Query(args.ToArray())).Success;
    }
    public Task<ProcessResult> Execute(Package p, string operation, Action<string> log) => runner.Run(Build(operation, p), log);
}

public sealed class ChocolateyProvider(ICommandRunner runner, Settings settings) : IPackageProvider
{
    public string Name => "chocolatey";
    public static string Executable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin", "choco.exe");
    string Source => Rules.ValidateSource(settings.ChocolateySource);
    async Task<ProcessResult> Query(params string[] args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        return await runner.Run(new(Executable, args), token: timeout.Token);
    }
    public async Task<string> Detect() { var r = await Query("--version"); r.EnsureSuccess(); return r.Output.Trim(); }
    static List<Package> Parse(string text) => text.Split('\n').Select(l => l.Trim().Split('|')).Where(p => p.Length == 2 && Rules.ValidId(p[0]) && Rules.KnownVersion(p[1])).Select(p => new Package { Provider = "chocolatey", Id = p[0], Name = p[0], Version = p[1], Scope = "machine" }).ToList();
    public async Task<List<Package>> Search(string query)
    {
        var r = await Query("search", query, "--limit-output", "--source", Source); r.EnsureSuccess(); return Parse(r.Output);
    }
    public async Task<List<Package>> Inventory()
    {
        var r = await Query("list", "--limit-output"); r.EnsureSuccess(); var packages = Parse(r.Output);
        var pins = await Query("pin", "list", "--limit-output");
        var pinnedIds = Parse(pins.Output).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in packages) p.Pinned = !pins.Success || pinnedIds.Contains(p.Id);
        // Only this configured source is queried. Never implicitly contact Community Repository.
        if (!string.IsNullOrWhiteSpace(settings.ChocolateySource))
        {
            var updates = await Query("outdated", "--limit-output", "--source", Source, "--ignore-unfound");
            if (updates.Code is not (0 or 2)) updates.EnsureSuccess();
            foreach (var row in updates.Output.Split('\n').Select(l => l.Trim().Split('|')).Where(v => v.Length >= 4))
                foreach (var p in packages.Where(p => p.Id.Equals(row[0], StringComparison.OrdinalIgnoreCase))) { p.Available = row[2]; p.Pinned |= row[3].Equals("true", StringComparison.OrdinalIgnoreCase); }
        }
        return packages;
    }
    public async Task<bool> Available(Package p)
    {
        Rules.Validate(p);
        var r = await Query("search", p.Id, "--exact", "--all-versions", "--limit-output", "--source", Source);
        r.EnsureSuccess();
        return Parse(r.Output).Any(x => x.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase) && (p.VersionPolicy == "Newest" || p.Version == x.Version));
    }
    public Command Build(string operation, Package p)
    {
        Rules.Validate(p);
        if (p.Scope == "user") throw new InvalidDataException("Chocolatey is machine-scoped in this release.");
        if (operation is not ("install" or "upgrade" or "uninstall")) throw new InvalidDataException("Unsupported operation.");
        List<string> args = [operation, p.Id, "--yes", "--no-progress", "--limit-output", "--use-package-exit-codes"];
        if (operation != "uninstall") { args.AddRange(["--source", Source]); if (p.VersionPolicy == "Captured") args.AddRange(["--version", p.Version]); }
        return new(Executable, args);
    }
    public Task<ProcessResult> Execute(Package p, string operation, Action<string> log) => runner.Run(Build(operation, p), log);
}
