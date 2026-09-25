using System.IO.Compression;
using Sterling.Core;
internal static class V012Checks
{
    public static async Task Run(string temp, Action<bool, string> check)
    {
        var known = Storage.Load<List<CommonApplication>>("assets/common-applications.json");
        check(Rules.NewerAvailable("1.0", "2.0") && !Rules.NewerAvailable("2.0", "1.0") && !Rules.NewerAvailable("unknown", "2.0") && !Rules.NewerAvailable("1.0", ">2.0"), "automatic updates reject older, bounded and uncertain versions");
        check(known.Select(a => a.Id).SequenceEqual(["Google.Chrome", "Mozilla.Firefox", "Brave.Brave", "Microsoft.Edge", "VideoLAN.VLC", "Microsoft.Office"]) && !known.Last().Enabled, "verified common IDs and Office deployment hold");
        Package P(string id = "Google.Chrome") => new() { Id = id, Scope = "machine", Version = "131.0.1" };
        var d = new PackageDetails("Google.Chrome", "winget", "154.0", "Google", "", "https://www.google.com", "wix", "", "", DateTimeOffset.UtcNow);
        check(PackageInformation.Categorize(P(), d, true, known) == "Ready for automatic install", "verified silent installer categorised automatic");
        var ambiguous = P(); ambiguous.MultipleProviders = true;
        check(PackageInformation.Categorize(ambiguous, d, true, known).Contains("Ambiguous"), "ambiguous captured package remains manual");
        check(PackageInformation.Categorize(P(), d, false, known).Contains("unavailable"), "missing captured version remains manual");
        var unknownScope = P(); unknownScope.Scope = "unknown";
        check(PackageInformation.Categorize(unknownScope, d, true, known).Contains("Scope unknown"), "unknown capture scope remains manual");
        var advisory = Storage.Load<List<SecurityFinding>>("assets/security-findings.json").Single();
        check(advisory.Affects(P("Mozilla.Firefox")) && !advisory.Affects(P("Mozilla.Firefox.ESR")), "security finding matches exact Firefox edition");
        var fixedVersion = P("Mozilla.Firefox"); fixedVersion.Version = "131.0.2";
        check(!advisory.Affects(fixedVersion) && !SecurityFinding.VersionRange("unknown", "131.0", "131.0.2"), "security version boundary excludes fixed and uncertain versions");
        check(HealthInterpretation.Interpret("CheckHealth", 0, "No component store corruption detected.").State == "Healthy", "health output identifies healthy image");
        check(HealthInterpretation.Interpret("ScanHealth", 0, "The component store is repairable.").State == "Corruption found", "health identifies corruption even with exit zero");
        check(HealthInterpretation.Interpret("RestoreHealth", 0, "The restore operation completed successfully.").State == "Repaired", "health identifies completed repair");
        check(HealthInterpretation.Interpret("SFC", 0, "Windows Resource Protection found corrupt files but was unable to fix some of them.").State == "Further action", "SFC incomplete repair is never passed");
        check(HealthInterpretation.Interpret("SFC", 0, "unrecognised localised text").State == "Further action" && HealthInterpretation.Interpret("CheckHealth", 5, "").State == "Failed", "launch success and unknown output cannot imply healthy");
        var printer = new PrinterItem { Name = "Test", PortKind = "TCP/IP", HostAddress = "192.0.2.123" };
        check(PrinterPlanning.Plan(printer, false).StartsWith("Driver") && PrinterPlanning.Plan(printer, true).StartsWith("Skip"), "printer restore plans order and default conflict skip");
        printer.PortKind = "Unsupported monitor"; check(PrinterPlanning.Plan(printer, false).StartsWith("Manual"), "unsupported printer monitor remains manual");
        try { PrinterPlanning.Validate("ARM64", "10.0.26100", "AMD64", "10.0.26100"); check(false, "architecture mismatch rejected"); } catch (InvalidDataException) { check(true, "printer architecture mismatch rejected"); }
        string files = Path.Combine(temp, "printer-bundle"); Directory.CreateDirectory(files); File.WriteAllText(Path.Combine(files, "manifest.json"), "{\"SchemaVersion\":1,\"Printers\":[]}"); File.WriteAllText(Path.Combine(files, "Inventory.txt"), "fixture");
        string zip = Path.Combine(temp, "PrinterBackup.zip"); IntegrityBundle.Pack(files, zip); string opened = await IntegrityBundle.Open(zip);
        check(File.Exists(Path.Combine(opened, "manifest.json")), "printer bundle created and integrity verified on open");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) { archive.GetEntry("Inventory.txt")!.Delete(); using var w = new StreamWriter(archive.CreateEntry("Inventory.txt").Open()); w.Write("changed"); }
        try { await IntegrityBundle.Open(zip); check(false, "changed bundle rejected"); } catch (InvalidDataException) { check(true, "changed printer bundle rejected before restoration"); }
        string user = Path.Combine(temp, "browser-user"), profile = Path.Combine(user, @"AppData\Local\Google\Chrome\User Data\Default"); Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "Bookmarks"), "{\"roots\":{\"bookmark_bar\":{\"children\":[]}}}");
        File.WriteAllText(Path.Combine(profile, "Preferences"), "{\"bookmark_bar\":{\"show_on_all_tabs\":true},\"browser\":{\"show_home_button\":true},\"account_info\":\"SECRET\"}");
        File.WriteAllText(Path.Combine(profile, "Login Data"), "SECRET"); var profiles = BrowserBackups.Detect(user);
        check(profiles.Count == 1 && !profiles[0].Selected && profiles[0].User == user, "browser detection binds selected Windows user and starts unchecked");
        profiles[0].Selected = true; profiles[0].Settings = true; string browserZip = Path.Combine(temp, "BrowserBackup.zip");
        var manifest = await BrowserBackups.Backup(profiles, browserZip, _ => { }); string browserRoot = await IntegrityBundle.Open(browserZip);
        check(!Directory.GetFiles(browserRoot, "*", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains("SECRET")), "browser allowlist excludes secrets and account preferences");
        string targetPath = Path.Combine(temp, "browser-target"); Directory.CreateDirectory(targetPath); File.WriteAllText(Path.Combine(targetPath, "Preferences"), "{\"account_info\":\"RETAIN\"}");
        var target = new BrowserProfile { Browser = "Chrome", Path = targetPath };
        string restored = await BrowserBackups.Restore(browserRoot, manifest.Profiles[0], target, true, true, _ => { });
        check(restored.Contains("verified") && File.ReadAllText(Path.Combine(targetPath, "Preferences")).Contains("RETAIN") && Directory.GetFiles(targetPath, "*.bak").Length == 1, "browser restore preserves unrelated settings and prior target");
        var sourceBefore = new Dictionary<string, PackageDetails> { ["winget:Google.Chrome"] = d }; var sourceAfter = new Dictionary<string, PackageDetails> { ["winget:Google.Chrome"] = d with { Publisher = "Changed metadata" } };
        check(SourceSnapshot.Compare(sourceBefore, sourceAfter, []).Single().Contains("does not establish ownership"), "source changes avoid unsupported ownership claims");
    }
}
