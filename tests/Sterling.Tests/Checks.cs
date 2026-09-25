using System.IO.Compression;
using System.Security.Cryptography;
using Sterling.Core;

int passed = 0;
string temp = Path.Combine(Path.GetTempPath(), "Sterling-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
Package P(string id = "Vendor.App") => new() { Id = id, Name = id, Version = "1.0", Scope = "machine" };
JobEngine Engine(FakeProvider fake) => new([fake], _ => { }, Path.Combine(temp, Guid.NewGuid().ToString("N")));

Check(Rules.ValidId("Notepad++.Notepad++"), "plus characters in exact IDs");
foreach (var id in new[] { "--all", "a;calc", "a\" b", "../x", "", "abc\nxyz", "…" }) Check(!Rules.ValidId(id), "reject unsafe ID " + id.Replace('\n', ' '));
foreach (var source in new[] { "https://community.chocolatey.org/api/v2", "https://chocolatey.org/api/v2", "http://internal/feed", "https://user:secret@internal/feed", "https://internal/feed?token=secret" }) Reject(() => Rules.ValidateSource(source), "reject unsafe/unapproved source");
Check(Rules.ValidateSource("https://packages.example.org/choco").StartsWith("https://packages.example.org"), "internal source allowed");
Check(!Rules.KnownVersion("Unknown") && !Rules.KnownVersion("> 2.0"), "uncertain versions excluded");
var settings = new Settings { AcceptSourceAgreements = true, ChocolateySource = "https://packages.example.org/choco" };
var winget = new WingetProvider(new NeverRun(), settings);
var package = P(); package.VersionPolicy = "Captured";
var command = winget.Build("install", package);
Check(command.Args.Contains("--exact") && command.Args.Contains("--version") && command.Args.Contains("--no-upgrade") && !command.Args.Contains("--force") && !command.Args.Contains("--allow-reboot"), "exact and conservative WinGet install arguments");
Check(command.Args[command.Args.ToList().IndexOf("--id") + 1] == package.Id, "ID is a separate argument");
var choco = new ChocolateyProvider(new NeverRun(), settings);
Check(choco.Build("upgrade", package).Args.Contains(settings.ChocolateySource), "Chocolatey source always explicit");
package.Scope = "user"; Reject(() => choco.Build("install", package), "Chocolatey per-user operation refused");
string table = "Name            Id                  Version     Available\n----------------------------------------------------------------\nExample App     Vendor.App          1.0         2.0\nAnother         Vendor.Other        Unknown     3.0\n";
var rows = WingetTable.Parse(table);
Check(rows.Count == 2 && rows[0]["Id"] == "Vendor.App" && rows[0]["Available"] == "2.0", "fixed-width table parsing");
Reject(() => WingetTable.Parse("Nom        Identifiant\n-----------------------\nExemple    Vendor.App"), "unrecognised/localised table fails closed");
var bundlePath = Path.Combine(temp, "bundle.json");
Storage.Save(bundlePath, new Bundle { Packages = [P(), new Package { Provider = "unmatched", Match = "Unknown", Name = "Custom app" }] });
var bundle = Storage.LoadBundle(bundlePath);
Check(bundle.Packages.Count == 2 && !bundle.Packages[1].Manageable && bundle.Packages[1].Disposition.Contains("Manual"), "capture round trip preserves manual items");
var unknownVersion = P(); unknownVersion.Version = "Unknown"; unknownVersion.VersionPolicy = "Captured";
Storage.Save(bundlePath, new Bundle { Packages = [unknownVersion] });
Check(Storage.LoadBundle(bundlePath).Packages.Single().VersionPolicy == "Captured", "unknown captured version never silently becomes newest");
Reject(() => Rules.Validate(unknownVersion), "unknown captured version blocked before execution");
Storage.Save(bundlePath, new Bundle { SchemaVersion = 3 }); Reject(() => Storage.LoadBundle(bundlePath), "future schema rejected");
var fake = new FakeProvider(); fake.Fail.Add("Vendor.Fail");
var items = new List<JobItem> { new() { Package = P("Vendor.Fail") }, new() { Package = P("Vendor.Good") } };
var engine = Engine(fake); await engine.Run(items);
Check(items[0].Status == "Failed" && items[1].Status == "Succeeded", "job continues after nonblocking failure");
fake.Fail.Clear(); await engine.Run(items);
Check(items.All(x => x.Status == "Succeeded") && fake.Executed.Count(x => x == "install:Vendor.Good") == 1, "retry excludes successful items");
fake = new(); var old = P(); old.Available = "2.0"; old.Pinned = true; fake.Installed.Add(old);
items = [new() { Package = old.Copy(), Operation = "upgrade" }]; await Engine(fake).Run(items);
Check(items[0].Status == "Failed" && fake.Executed.Count == 0, "pinned upgrade never executed");
fake = new() { IsAvailable = false }; fake.Installed.Add(P());
package = P(); package.VersionPolicy = "Captured"; package.Version = "0.5";
items = [new() { Package = package, Operation = "replace" }]; await Engine(fake).Run(items);
Check(items[0].Status == "Failed" && fake.Executed.Count == 0, "replacement checks availability before uninstall");
fake = new(); fake.Installed.Add(P()); items = [new() { Package = package }]; await Engine(fake).Run(items);
Check(items[0].Status == "Failed" && fake.Executed.Count == 0, "captured version never silently downgraded");
fake = new(); fake.Installed.Add(P()); items = [new() { Package = P() }]; await Engine(fake).Run(items);
Check(items[0].Status == "Skipped" && fake.Executed.Count == 0, "satisfied list skips existing package");
fake = new(); old = P(); old.Available = "2.0"; fake.Installed.Add(old); items = [new() { Package = P() }]; await Engine(fake).Run(items);
Check(items[0].Status == "Succeeded" && fake.Executed.Single() == "upgrade:Vendor.App", "newest list upgrades eligible older install");
fake = new() { ChangeState = false }; items = [new() { Package = P() }]; await Engine(fake).Run(items);
Check(items[0].Status == "Needs review", "unverified success held for manual review");
fake = new() { FailVerification = true }; items = [new() { Package = P() }]; engine = Engine(fake); await engine.Run(items); await engine.Run(items);
Check(items[0].Status == "Needs review" && fake.Executed.Count == 1, "verification exception never replays successful command");
fake = new(); using var cancel = new CancellationTokenSource(); cancel.Cancel(); items = [new() { Package = P() }]; await Engine(fake).Run(items, cancel.Token);
Check(items[0].Status == "Cancelled" && fake.Executed.Count == 0, "cancel stops unstarted work");
Check(new ProcessResult(3010, "").Success && new ProcessResult(unchecked((int)0x8A150109), "").RestartRequired && !new ProcessResult(unchecked((int)0x8A15010A), "").Success, "restart-required results distinguished");
foreach (var path in new[] { "../escape.exe", "C:\\outside.exe", "a/../../x", "file:stream", "dir./a" }) Reject(() => AppUpdates.SafePath(temp, path), "updater rejects path escape/alias");
string artifact = Path.Combine(temp, "artifact"); File.WriteAllText(artifact, "test release");
string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artifact))); await AppUpdates.Verify(artifact, hash); Check(true, "valid update hash accepted");
try { await AppUpdates.Verify(artifact, new string('0', 64)); throw new Exception("corrupt accepted"); } catch (InvalidDataException) { Check(true, "corrupt update rejected"); }
Reject(() => AppUpdates.ExpectedHash(hash + "  wrong.zip", "release.zip"), "checksum filename binding");
string zip = Path.Combine(temp, "bad.zip"); using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("../evil.exe").Open()); writer.Write("bad"); }
Reject(() => AppUpdates.ExtractSafe(zip, Path.Combine(temp, "extract")), "zip traversal blocked");
string stage = Path.Combine(temp, "stage"), target = Path.Combine(temp, "target"), backup = Path.Combine(temp, "backup"); Directory.CreateDirectory(stage); Directory.CreateDirectory(target);
File.WriteAllText(Path.Combine(target, "sterling-portable.json"), "{}"); File.WriteAllText(Path.Combine(target, "app.dll"), "old"); File.WriteAllText(Path.Combine(stage, "app.dll"), "new"); File.WriteAllText(Path.Combine(stage, "new.dll"), "extra");
try { AppUpdates.Apply(stage, target, backup, n => { if (n == 2) throw new IOException("Simulated copy failure"); }); } catch (IOException) { }
Check(File.ReadAllText(Path.Combine(target, "app.dll")) == "old" && !File.Exists(Path.Combine(target, "new.dll")), "failed update restores old files and removes new files");
AppUpdates.Apply(stage, target, Path.Combine(temp, "backup-success")); Check(File.ReadAllText(Path.Combine(target, "app.dll")) == "new", "successful update copies replacement");
File.WriteAllText(Path.Combine(target, "customer-capture.sterling.json"), "customer inventory");
Directory.CreateDirectory(Path.Combine(target, "presets")); File.WriteAllText(Path.Combine(target, "presets", "customer.json"), "customer preset");
AppUpdates.Apply(stage, target, Path.Combine(temp, "backup-preservation"));
Check(File.ReadAllText(Path.Combine(target, "customer-capture.sterling.json")) == "customer inventory" && File.ReadAllText(Path.Combine(target, "presets", "customer.json")) == "customer preset", "updater preserves captures and presets beside binaries");
string chromeRoot = Path.Combine(temp, "chrome"), bookmarkInput = Path.Combine(temp, "bookmarks.json");
File.WriteAllText(bookmarkInput, "{\"roots\":{\"bookmark_bar\":{\"children\":[]}}}");
var chrome = P("Google.Chrome"); chrome.Data.BookmarksFile = bookmarkInput; chrome.Data.Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bookmarkInput))); chrome.Data.RestoreBookmarks = true;
var recipe = new BookmarkRecipe(chromeRoot, () => false);
string bookmarkTarget = recipe.Target(chrome.Data); Directory.CreateDirectory(Path.GetDirectoryName(bookmarkTarget)!); File.WriteAllText(bookmarkTarget, "{\"roots\":{\"bookmark_bar\":{\"name\":\"old\"}}}");
await recipe.Restore(chrome);
Check(File.ReadAllText(bookmarkTarget) == File.ReadAllText(bookmarkInput) && Directory.GetFiles(Path.GetDirectoryName(bookmarkTarget)!, "*.bak").Length == 1, "bookmark recipe verifies restore and preserves original");
chrome.Data.ChromeProfile = "../other-user"; Reject(() => recipe.Target(chrome.Data), "recipe rejects profile path escape"); chrome.Data.ChromeProfile = "Default";
Storage.SaveBundle(bundlePath, new Bundle { Packages = [chrome] });
var loadedCapture = Storage.LoadBundle(bundlePath);
Check(!loadedCapture.Packages[0].Data.RestoreBookmarks && File.Exists(loadedCapture.Packages[0].Data.BookmarksFile), "portable v2 capture includes data attachment but resets optional restore");
Check(!File.ReadAllText(bundlePath).Contains(temp.Replace("\\", "\\\\")), "portable bundle stores relative attachment path");
Storage.SaveBundle(bundlePath, new Bundle { Kind = "list", ExplicitPresetOptions = true, Packages = [chrome] });
Check(Storage.LoadBundle(bundlePath).Packages[0].Data.RestoreBookmarks, "explicit preset preserves selected data option");
Storage.Save(bundlePath, new Bundle { SchemaVersion = 1, Packages = [chrome] });
Check(Storage.LoadBundle(bundlePath).Packages[0].Data.BookmarksFile == "", "v1 capture migration defaults to no data actions");
File.WriteAllText(bundlePath, "{\"SchemaVersion\":2,\"Kind\":\"capture\",\"Packages\":[{\"Id\":\"Google.Chrome\",\"Data\":{\"BookmarksFile\":\"../outside.json\"}}]}");
Reject(() => Storage.LoadBundle(bundlePath), "import rejects data attachment escape");
var proposedJobs = ReviewBuilder.Jobs([chrome], "install");
Check(proposedJobs.Count == 2 && proposedJobs[1].DependsOnIndex == 0, "review builds ordered installation and optional data step");
var review = ReviewBuilder.Describe(proposedJobs[1], [], new Settings(), recipe);
Check(!review.Blocked && review.Notes.Contains(bookmarkTarget), "data review exposes actual target profile location");
chrome.Excluded = true;
Check(ReviewBuilder.Describe(new() { Package = chrome }, [], new Settings(), recipe).Blocked, "review blocks policy exclusions before Start");
fake = new(); fake.Fail.Add("Google.Chrome"); var dataRunner = new FakeDataRecipe();
var dependencyJobs = ReviewBuilder.Jobs([proposedJobs[0].Package], "install");
await new JobEngine([fake], _ => {}, Path.Combine(temp, "step-job"), dataRunner).Run(dependencyJobs);
Check(dataRunner.Count == 0 && dependencyJobs.All(j => j.Status == "Failed"), "data step never runs after failed software prerequisite");
fake.Fail.Clear(); await new JobEngine([fake], _ => {}, Path.Combine(temp, "step-job"), dataRunner).Run(dependencyJobs);
Check(dataRunner.Count == 1 && dependencyJobs.All(j => j.Status == "Succeeded"), "retry recovers failed installation then runs dependent data once");
await V012Checks.Run(temp, Check);
if (args.Contains("--live-winget"))
{
    var live = new WingetProvider(new ProcessRunner(), new Settings());
    Check((await live.Detect()).StartsWith('v'), "live WinGet detection");
    var results = await live.Search("7zip"); Check(results.Any(p => p.Id == "7zip.7zip"), "live WinGet exact search parsing");
    var installed = await live.Inventory(); Check(installed.Count > 0 && installed.All(p => p.Scope is "user" or "machine"), "live scope-aware inventory parsing");
    Console.WriteLine($"Live inventory rows: {installed.Count}; eligible updates: {installed.Count(p => p.Eligible)}");
}
Console.WriteLine($"{passed} checks passed. Test data: {temp}");

sealed class NeverRun : ICommandRunner { public Task<ProcessResult> Run(Command command, Action<string>? log = null, CancellationToken token = default) => throw new Exception("Unexpected process launch"); }
sealed class FakeDataRecipe : IDataRecipeRunner { public int Count; public string Describe(Package p) => "test"; public Task<string> Restore(Package p) { Count++; return Task.FromResult("restored"); } }
sealed class FakeProvider : IPackageProvider
{
    public string Name => "winget";
    public List<Package> Installed = [];
    public List<string> Executed = [];
    public HashSet<string> Fail = [];
    public bool IsAvailable = true, ChangeState = true, FailVerification;
    public Task<string> Detect() => Task.FromResult("test");
    public Task<List<Package>> Search(string query) => Task.FromResult(new List<Package>());
    public Task<List<Package>> Inventory() { if (FailVerification && Executed.Count > 0) throw new IOException("Inventory unavailable after successful installer"); return Task.FromResult(Installed.Select(p => p.Copy()).ToList()); }
    public Task<bool> Available(Package p) => Task.FromResult(IsAvailable);
    public Task<ProcessResult> Execute(Package p, string operation, Action<string> log)
    {
        Executed.Add(operation + ":" + p.Id);
        if (Fail.Contains(p.Id)) return Task.FromResult(new ProcessResult(1, "Simulated failure"));
        if (ChangeState) { Installed.RemoveAll(x => x.Id == p.Id); if (operation != "uninstall") { var copy = p.Copy(); copy.Available = ""; Installed.Add(copy); } }
        return Task.FromResult(new ProcessResult(0, ""));
    }
}
