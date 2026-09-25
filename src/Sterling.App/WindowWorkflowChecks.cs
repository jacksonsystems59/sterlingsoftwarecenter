using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Sterling.Core;
namespace Sterling.App;

internal static class WindowWorkflowChecks
{
    public static async Task Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        string sandbox = Path.Combine(Path.GetTempPath(), "Sterling-ui-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(sandbox);
        var provider = new UiTestProvider();
        var recipe = new BookmarkRecipe(Path.Combine(sandbox, "Chrome"), () => false);
        var window = new MainWindow(new Settings { CheckAppUpdates = false }, [provider], recipe, Path.Combine(sandbox, "jobs"));
        Application.Current.MainWindow = window; window.Show();
        await window.RunWorkflowChecks(provider, recipe, sandbox, output);
        window.Close();
    }
}
public partial class MainWindow
{
    internal async Task RunWorkflowChecks(UiTestProvider fake, BookmarkRecipe testRecipe, string sandbox, string output)
    {
        var checks = new List<string>();
        void Assert(bool value, string name) { if (!value) throw new InvalidOperationException("UI test failed: " + name); checks.Add(name); }
        async Task Settled()
        {
            for (int i = 0; i < 500 && busy; i++) await Task.Delay(10);
            if (busy) throw new TimeoutException("UI operation did not finish");
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        void Snapshot(string name) => App.Render(this, Path.Combine(output, name + ".png"));
        Package P(string id, string name) => new() { Id = id, Name = name, Scope = "machine", Version = "1.0" };
        basket.Add(P("Google.Chrome", "Google Chrome")); basket.Add(P("7zip.7zip", "7-Zip"));
        Install_Click(this, new RoutedEventArgs()); await Settled();
        Assert(Tabs.SelectedIndex == 5 && reviewRows.Count == 2, "Install selection reviews all packages inside the main window");
        Assert(Application.Current.Windows.Cast<Window>().Count(w => w.IsVisible) == 1, "No floating package review window");
        Assert(fake.Executed.Count == 0 && !ReviewStartButton.IsEnabled, "Review preparation and unaccepted review do not execute jobs");
        Snapshot("review-install");
        ReviewConsent.IsChecked = true; ReviewCancel_Click(this, new RoutedEventArgs());
        Assert(fake.Executed.Count == 0 && pendingJobs.Count == 0, "Back cancels install review without package changes");
        var installed = P("7zip.7zip", "7-Zip"); installed.Available = "2.0"; installed.Selected = true; fake.Installed.Add(installed.Copy()); inventory.Add(installed); Tabs.SelectedIndex = 1;
        UpdateSelected_Click(this, new RoutedEventArgs()); await Settled();
        Assert(Tabs.SelectedIndex == 3 && reviewRows.Count == 0, "Update ticked starts directly in Jobs without Review");
        Assert(fake.Executed.SequenceEqual(["upgrade:7zip.7zip"]) && jobs.Single().Status == "Succeeded", "Direct update executes and verifies eligible package");
        Snapshot("direct-update");
        installed.Excluded = true; UpdateSelected_Click(this, new RoutedEventArgs()); await Settled();
        Assert(jobs.Single().Status == "Skipped" && fake.Executed.Count == 1, "Direct updates skip exclusions without executing package changes");
        installed.Excluded = false;
        Assert(commonApps.Count == 6 && commonApps.Single(a => a.Id == "Microsoft.Office").Enabled == false, "Common applications use configured IDs and hold unverified Office deployment");
        SearchBox.Text = "fire"; Assert(suggestions.Items.Cast<string>().Any(s => s.Contains("Firefox")) && fake.Executed.Count == 1, "Typing suggestions never installs applications");
        availableRelease = new("9.0.0", "", "", "", ""); RefreshUpdateIndicator();
        Assert(updateIndicator.Visibility == Visibility.Visible && updateIndicator.Content.ToString()!.Contains("9.0.0"), "Newer release produces the top-right update indicator");
        availableRelease = null; RefreshUpdateIndicator();
        string legacy = Path.Combine(sandbox, "legacy-v010.sterling.json");
        File.WriteAllText(legacy, """
        {"SchemaVersion":1,"Kind":"capture","Name":"v0.1.0 customer capture","Packages":[{"Provider":"winget","Id":"Google.Chrome","Name":"Google Chrome","Version":"1.0","Scope":"machine","Match":"Provider match","VersionPolicy":"Captured"},{"Provider":"winget","Id":"7zip.7zip","Name":"7-Zip","Version":"1.0","Scope":"machine","Match":"Provider match","VersionPolicy":"Captured"}]}
        """);
        Tabs.SelectedIndex = 2; LoadCapture(legacy); await Settled();
        Assert(restored.Count == 2 && DataRecipePanel.IsEnabled && !restored[0].Data.RestoreBookmarks, "v0.1.0 capture opens with Chrome detail and optional data unchecked");
        RestoreGrid.SelectedItem = restored[1]; await Settled();
        Assert(!DataRecipePanel.IsEnabled && DataRecipePanel.Visibility == Visibility.Collapsed && DataRecipeStatus.Text.Contains("No tested"), "Unsupported application clearly states no tested data recipe");
        RestoreGrid.SelectedItem = restored[0];
        var chrome = restored[0]; string source = Path.Combine(sandbox, "bookmarks.json");
        File.WriteAllText(source, "{\"roots\":{\"bookmark_bar\":{\"children\":[],\"name\":\"Restored test\"}}}");
        chrome.Data.BookmarksFile = source; chrome.Data.Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        string target = testRecipe.Target(chrome.Data); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, "{\"roots\":{\"bookmark_bar\":{\"name\":\"Original test\"}}}");
        RestoreDataBox.IsChecked = true; ShowDataLocations();
        foreach (var p in restored) { p.Selected = true; p.VersionPolicy = "Newest"; p.Changed(nameof(p.VersionPolicy)); }
        await Settled(); Snapshot("capture-details");
        ReviewRestore_Click(this, new RoutedEventArgs()); await Settled();
        Assert(reviewRows.Count == 3 && reviewRows[1].Action == "Restore bookmarks" && reviewRows[1].Notes.Contains(target), "Bulk restore includes explicit per-application data step and target location");
        ReviewCancel_Click(this, new RoutedEventArgs());
        Assert(File.ReadAllText(target).Contains("Original test") && fake.Executed.Count == 1, "Cancelling restore leaves software and profile data untouched");
        ReviewRestore_Click(this, new RoutedEventArgs()); await Settled(); ReviewConsent.IsChecked = true; ReviewStart_Click(this, new RoutedEventArgs()); await Settled();
        Assert(jobs.Count == 3 && jobs.All(j => j.Status is "Succeeded" or "Skipped") && File.ReadAllText(target).Contains("Restored test"), "Approved restore executes installation before its selected bookmark recipe");
        Assert(Directory.GetFiles(Path.GetDirectoryName(target)!, "Bookmarks.sterling-*.bak").Length == 1, "Recipe retains previous bookmarks");
        Snapshot("job-results");
        Assert(Icon != null && FindResource("Icon.app") != null && FindResource("Icon.install") != null && FindResource("Icon.failure") != null, "Application and action icons are loaded");
        Storage.Save(Path.Combine(output, "checks.json"), new { Passed = checks.Count, Checks = checks });
    }
}
internal sealed class UiTestProvider : IPackageProvider
{
    public string Name => "winget";
    public List<Package> Installed { get; } = [];
    public List<string> Executed { get; } = [];
    public Task<string> Detect() => Task.FromResult("test provider");
    public Task<List<Package>> Search(string query) => Task.FromResult(new List<Package>());
    public Task<List<Package>> Inventory() => Task.FromResult(Installed.Select(p => p.Copy()).ToList());
    public Task<bool> Available(Package p) => Task.FromResult(true);
    public Task<ProcessResult> Execute(Package p, string operation, Action<string> log)
    {
        Executed.Add(operation + ":" + p.Id);
        Installed.RemoveAll(x => x.Id == p.Id);
        if (operation != "uninstall") { var copy = p.Copy(); copy.Available = ""; if (operation == "upgrade") copy.Version = "2.0"; Installed.Add(copy); }
        return Task.FromResult(new ProcessResult(0, "Test provider; no package-manager process was launched."));
    }
}
