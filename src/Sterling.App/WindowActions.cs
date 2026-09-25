using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Sterling.Core;
namespace Sterling.App;
public partial class MainWindow : Window
{
    readonly ObservableCollection<Package> basket = [], inventory = [], restored = [];
    readonly ObservableCollection<JobItem> jobs = [];
    readonly Settings settings;
    readonly List<IPackageProvider> providers;
    readonly JobEngine engine;
    readonly string settingsFile = Path.Combine(Storage.Home, "settings.json");
    readonly string logFile = Path.Combine(Storage.Home, "logs", DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
    readonly AppUpdates appUpdates = new();
    AppRelease? availableRelease;
    CancellationTokenSource? jobStop;
    bool busy, updateClosing;
    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        try { settings = File.Exists(settingsFile) ? Storage.Load<Settings>(settingsFile) : new(); }
        catch (Exception ex) { settings = new(); Log("Settings could not be loaded: " + ex.Message); }
        providers = [new WingetProvider(new ProcessRunner(), settings), new ChocolateyProvider(new ProcessRunner(), settings)];
        engine = new(providers, Log);
        BasketGrid.ItemsSource = basket; InventoryGrid.ItemsSource = inventory; RestoreGrid.ItemsSource = restored; JobsGrid.ItemsSource = jobs;
        basket.CollectionChanged += (_, _) => { if (!busy) StatusText.Text = basket.Count + " packages selected for deployment"; };
        BasketScope.ItemsSource = RestoreScope.ItemsSource = new[] { "unknown", "user", "machine" };
        BasketPolicy.ItemsSource = RestorePolicy.ItemsSource = new[] { "Newest", "Captured" };
        ChocoSourceBox.Text = settings.ChocolateySource; AgreementBox.IsChecked = settings.AcceptSourceAgreements; AppUpdateBox.IsChecked = settings.CheckAppUpdates;
        VersionLabel.Text = "v" + CurrentVersion.ToString(3) + "  ·  WINDOWS x64";
        Subtitle.Text = Environment.MachineName + "  /  " + Environment.UserName + "  /  LOCAL WORKSPACE";
        Closing += (_, e) => { if (busy && !updateClosing) { e.Cancel = true; StatusText.Text = "Operation running. Use Stop after current package before closing."; } };
        if (!App.SmokeMode) Loaded += async (_, _) => await Guard(async () => { await Detect(); LoadPreviousJob(); if (settings.CheckAppUpdates) { try { await CheckAppUpdate(); } catch (Exception ex) { Log("App update check: " + ex.Message); } } });
    }
    static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version!;
    async Task Guard(Func<Task> action)
    {
        if (busy) { StatusText.Text = "An operation is already running. See Jobs & logs."; return; }
        busy = true;
        try { await action(); } catch (Exception ex) { StatusText.Text = ex.Message; Log(ex.ToString()); } finally { busy = false; }
    }
    void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Dispatcher.InvokeAsync(() =>
        {
            var line = DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine;
            File.AppendAllText(logFile, line);
            if (LogBox.Text.Length > 180000) LogBox.Text = LogBox.Text[^90000..];
            LogBox.AppendText(line); LogBox.ScrollToEnd();
        });
    }
    void LoadPreviousJob()
    {
        var path = Path.Combine(Storage.Home, "last-job.json"); if (!File.Exists(path)) return;
        try { foreach (var item in Storage.Load<List<JobItem>>(path)) { if (item.Status is "Checking" or "Running" or "Uninstalling" or "Verifying" or "Queued") { item.Status = "Needs review"; item.Detail = "Previous session interrupted. Refresh installed state before creating a new job."; } jobs.Add(item); } }
        catch (Exception ex) { Log("Previous job could not be loaded: " + ex.Message); }
    }
    IPackageProvider Provider(string name) => providers.Single(p => p.Name == name);
    string SearchProvider => (ProviderBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "winget";
    async Task Detect()
    {
        List<string> statuses = [];
        foreach (var p in providers) try { statuses.Add(p.Name + ": " + await p.Detect()); } catch (Exception ex) { statuses.Add(p.Name + ": unavailable"); Log(p.Name + " detection: " + ex.Message); }
        ProvidersStatus.Text = string.Join("    |    ", statuses); StatusText.Text = ProvidersStatus.Text;
    }
    async void Detect_Click(object s, RoutedEventArgs e) => await Guard(Detect);
    async void Search_Click(object s, RoutedEventArgs e) => await Guard(Search);
    async void Search_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await Guard(Search); } }
    async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) throw new InvalidOperationException("Enter an application name or package ID.");
        StatusText.Text = "Searching " + SearchProvider + "…";
        var results = await Provider(SearchProvider).Search(SearchBox.Text.Trim());
        foreach (var p in results)
        {
            p.Selected = basket.Any(x => x.Provider == p.Provider && x.Id == p.Id);
            p.PropertyChanged += (_, e) => { if (e.PropertyName != nameof(Package.Selected)) return; if (p.Selected) AddBasket(p); else foreach (var existing in basket.Where(x => x.Provider == p.Provider && x.Id == p.Id).ToList()) basket.Remove(existing); };
        }
        SearchGrid.ItemsSource = results; StatusText.Text = results.Count + " results · " + basket.Count + " selected for deployment";
    }
    void AddBasket(Package p) { if (p.Manageable && !p.Excluded && !basket.Any(x => x.Key == p.Key)) { var copy = p.Copy(); copy.Selected = false; basket.Add(copy); } }
    void RemoveBasket_Click(object s, RoutedEventArgs e) { if (busy) return; foreach (Package p in BasketGrid.SelectedItems.Cast<Package>().ToList()) basket.Remove(p); SyncSearch(); }
    void ClearBasket_Click(object s, RoutedEventArgs e) { if (busy) return; basket.Clear(); SyncSearch(); }
    void SyncSearch() { if (SearchGrid.ItemsSource is IEnumerable<Package> rows) foreach (var p in rows.ToList()) p.Selected = basket.Any(x => x.Provider == p.Provider && x.Id == p.Id); }
    void CommitEdits() { foreach (var grid in new[] { BasketGrid, InventoryGrid, RestoreGrid, SearchGrid }) { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); } }
    bool Confirm(string text) => MessageBox.Show(this, text, "Sterling Software Centre · review action", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    async Task StartJob(IEnumerable<Package> packages, string operation)
    {
        CommitEdits(); var chosen = packages.Select(p => p.Copy()).DistinctBy(p => p.Key).ToList();
        if (chosen.Count == 0) throw new InvalidOperationException("No eligible packages selected.");
        foreach (var p in chosen) { Rules.Validate(p); p.Excluded |= settings.Exclusions.Contains(p.Provider + ":" + p.Id); if (p.Excluded) throw new InvalidOperationException(p.Id + " is excluded. Review its policy first."); }
        string warning = operation is "uninstall" or "replace" ? "Uninstalling may remove settings or application data and affect licence activation. Replacement is not an automatic rollback. Back up application data first." : "You authorise these changes and accept the package licence agreements. Installers may request UAC elevation. Sterling does not request a restart.";
        if (!Confirm(operation.ToUpperInvariant() + " " + chosen.Count + " package(s):\n\n" + string.Join("\n", chosen.Take(15).Select(p => p.Provider + " / " + p.Id + " [" + p.Scope + ", " + p.VersionPolicy + "]")) + "\n\n" + warning)) return;
        jobs.Clear(); foreach (var p in chosen) jobs.Add(new JobItem { Package = p, Operation = operation }); Tabs.SelectedIndex = 3; await RunJob();
    }
    async Task RunJob()
    {
        jobStop?.Dispose(); jobStop = new(); StatusText.Text = "Running sequential package job. Other packages continue after a failure.";
        await engine.Run(jobs, jobStop.Token);
        StatusText.Text = $"Job finished · {jobs.Count(j => j.Status == "Succeeded")} succeeded · {jobs.Count(j => j.Status == "Skipped")} skipped · {jobs.Count(j => j.Status == "Failed")} failed · {jobs.Count(j => j.Status == "Needs review")} need review" + (jobs.Any(j => j.RestartRequired) ? " · Restart required" : "");
    }
    async void Install_Click(object s, RoutedEventArgs e) => await Guard(() => StartJob(basket, "install"));
    async void Retry_Click(object s, RoutedEventArgs e) => await Guard(async () =>
    {
        if (!jobs.Any(j => j.Status == "Failed")) throw new InvalidOperationException("No failed items to retry.");
        if (!Confirm("Retry failed items only? Failed replacements may already have uninstalled the previous version.")) return;
        foreach (var item in jobs.Where(j => j.Status == "Failed")) item.Package.Excluded = settings.Exclusions.Contains(item.Package.Provider + ":" + item.Package.Id);
        foreach (var item in jobs.Where(j => j.Status == "Cancelled")) item.Status = "Not retried";
        await RunJob();
    });
    void Stop_Click(object s, RoutedEventArgs e) { jobStop?.Cancel(); StatusText.Text = "Stop requested. The current installer will finish; remaining packages will be cancelled."; }
    async Task RefreshInventory()
    {
        inventory.Clear(); StatusText.Text = "Reading installed applications and applicable updates…"; List<string> warnings = [];
        foreach (var p in providers) try { foreach (var item in await p.Inventory()) inventory.Add(item); } catch (Exception ex) { warnings.Add(p.Name); Log("Inventory incomplete for " + p.Name + ": " + ex.Message); }
        foreach (var item in ReadRegistry()) if (!inventory.Any(p => p.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase) && p.Version == item.Version && p.Scope == item.Scope)) inventory.Add(item);
        static string Normal(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        foreach (var group in inventory.Where(p => p.Manageable).GroupBy(p => Normal(p.Name))) if (group.Select(p => p.Provider).Distinct().Count() > 1) foreach (var p in group) p.MultipleProviders = true;
        foreach (var p in inventory) p.Excluded = settings.Exclusions.Contains(p.Provider + ":" + p.Id);
        StatusText.Text = $"{inventory.Count} inventory rows · {inventory.Count(p => p.Eligible)} eligible updates" + (warnings.Count > 0 ? " · Provider unavailable/incomplete: " + string.Join(", ", warnings) + ". See logs." : "");
    }
    static IEnumerable<Package> ReadRegistry()
    {
        List<Package> result = [];
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser }) foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view); using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"); if (uninstall == null) continue;
            foreach (var name in uninstall.GetSubKeyNames()) try
            {
                using var key = uninstall.OpenSubKey(name); string display = key?.GetValue("DisplayName") as string ?? "";
                if (string.IsNullOrWhiteSpace(display) || key?.GetValue("SystemComponent") is int flag && flag == 1) continue;
                result.Add(new Package { Provider = "unmatched", Id = "", Name = display, Version = key?.GetValue("DisplayVersion") as string ?? "", Scope = hive == RegistryHive.CurrentUser ? "user" : "machine", Match = "Unknown — manual review", Disposition = "Manual attention / custom installer" });
            }
            catch (System.Security.SecurityException) { }
        }
        return result.DistinctBy(p => (p.Name, p.Version, p.Scope));
    }
    async void Inventory_Click(object s, RoutedEventArgs e) => await Guard(RefreshInventory);
    void SelectInventory_Click(object s, RoutedEventArgs e) { if (!busy) foreach (var p in inventory) p.Selected = true; }
    void ClearInventory_Click(object s, RoutedEventArgs e) { if (!busy) foreach (var p in inventory) p.Selected = false; }
    async void UpdateSelected_Click(object s, RoutedEventArgs e) => await Guard(() => StartJob(inventory.Where(p => p.Selected && p.Eligible), "upgrade"));
    async void UpdateAll_Click(object s, RoutedEventArgs e) => await Guard(async () => { await RefreshInventory(); await StartJob(inventory.Where(p => p.Eligible), "upgrade"); });
    async void Uninstall_Click(object s, RoutedEventArgs e) => await Guard(() => StartJob(inventory.Where(p => p.Selected), "uninstall"));
    void Exclude_Click(object s, RoutedEventArgs e) => SetExclusions(true);
    void Unexclude_Click(object s, RoutedEventArgs e) => SetExclusions(false);
    void SetExclusions(bool exclude)
    {
        if (busy) return;
        foreach (var p in inventory.Where(p => p.Selected && p.Manageable)) { string key = p.Provider + ":" + p.Id; if (exclude) settings.Exclusions.Add(key); else settings.Exclusions.Remove(key); p.Excluded = exclude; p.Changed(nameof(p.State)); }
        Storage.Save(settingsFile, settings); StatusText.Text = "Exclusion policy saved.";
    }
    async void Capture_Click(object s, RoutedEventArgs e) => await Guard(async () => { await RefreshInventory(); foreach (var p in inventory) p.Selected = true; StatusText.Text = "Capture prepared. Untick items to exclude, then Save ticked capture. Application-data backup is separate."; });
    string? SavePath(string filename) { var d = new SaveFileDialog { Filter = "Sterling bundle (*.sterling.json)|*.sterling.json|JSON (*.json)|*.json", FileName = filename, AddExtension = true }; return d.ShowDialog(this) == true ? d.FileName : null; }
    string? OpenPath(string filter) { var d = new OpenFileDialog { Filter = filter }; return d.ShowDialog(this) == true ? d.FileName : null; }
    async void SaveCapture_Click(object s, RoutedEventArgs e) => await Guard(() =>
    {
        CommitEdits(); var items = inventory.Where(p => p.Selected).Select(p => p.Copy()).ToList(); if (items.Count == 0) throw new InvalidOperationException("Tick inventory items to include in the capture.");
        var path = SavePath(Environment.MachineName + "-capture.sterling.json"); if (path == null) return Task.CompletedTask;
        if (string.Equals(Path.GetPathRoot(path), Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), StringComparison.OrdinalIgnoreCase) && !Confirm("This capture is on the Windows volume. It may be erased during reinstall. Save another copy to an external drive or network location before erasing Windows. Save here anyway?")) return Task.CompletedTask;
        foreach (var p in items) { p.Selected = false; p.VersionPolicy = "Captured"; if (!Rules.KnownVersion(p.Version)) p.Disposition = "Captured version unknown — review policy"; }
        Storage.Save(path, new Bundle { Name = Environment.MachineName + " capture", Packages = items }); StatusText.Text = "Capture saved: " + path; return Task.CompletedTask;
    });
    async void SaveList_Click(object s, RoutedEventArgs e) => await Guard(() =>
    {
        CommitEdits(); if (basket.Count == 0) throw new InvalidOperationException("Add packages before saving a deployment list."); foreach (var p in basket) Rules.Validate(p);
        var path = SavePath("Standard Workstation.sterling.json"); if (path != null) { Storage.Save(path, new Bundle { Kind = "list", Name = Path.GetFileNameWithoutExtension(path), Packages = basket.Select(p => p.Copy()).ToList() }); StatusText.Text = "Deployment list saved: " + path; } return Task.CompletedTask;
    });
    async void OpenBundle_Click(object s, RoutedEventArgs e) => await Guard(() =>
    {
        var path = OpenPath("Sterling / JSON bundles|*.json"); if (path == null) return Task.CompletedTask; var b = Storage.LoadBundle(path); restored.Clear();
        foreach (var p in b.Packages) { p.Excluded |= settings.Exclusions.Contains(p.Provider + ":" + p.Id); restored.Add(p); }
        BundleLabel.Text = b.Name + " · " + b.CapturedAt.ToLocalTime().ToString("g"); StatusText.Text = $"{restored.Count} items loaded; review and tick items to restore."; return Task.CompletedTask;
    });
    void Preset_Click(object s, RoutedEventArgs e)
    {
        if (busy) return; restored.Clear(); foreach (var (id, name) in new[] { ("Google.Chrome", "Google Chrome"), ("7zip.7zip", "7-Zip"), ("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader") }) restored.Add(new Package { Id = id, Name = name, Scope = "machine", Selected = true }); BundleLabel.Text = "Standard Workstation · editable starter list";
    }
    void SelectRestore_Click(object s, RoutedEventArgs e) { if (!busy) foreach (var p in restored) p.Selected = p.Manageable && !p.Excluded; }
    void CapturedPolicy_Click(object s, RoutedEventArgs e) => SetPolicy("Captured");
    void NewestPolicy_Click(object s, RoutedEventArgs e) => SetPolicy("Newest");
    void SetPolicy(string policy) { if (busy) return; CommitEdits(); foreach (var p in restored) { p.VersionPolicy = policy; p.Changed(nameof(p.VersionPolicy)); } }
    async void Availability_Click(object s, RoutedEventArgs e) => await Guard(async () =>
    {
        CommitEdits(); foreach (var p in restored.Where(p => p.Selected)) { try { p.Disposition = p.Manageable && await Provider(p.Provider).Available(p) ? "Version available" : "Unavailable / manual attention"; } catch (Exception ex) { p.Disposition = "Check failed"; Log(p.Id + ": " + ex.Message); } p.Changed(nameof(p.Disposition)); } StatusText.Text = "Availability checked. Versions are checked again before installation.";
    });
    void RestoreQueue_Click(object s, RoutedEventArgs e) { if (busy) return; CommitEdits(); foreach (var p in restored.Where(p => p.Selected)) AddBasket(p); Tabs.SelectedIndex = 0; StatusText.Text = basket.Count + " packages selected. Manual and excluded items were not added."; }
    async void RestoreUninstall_Click(object s, RoutedEventArgs e) => await Guard(() => StartJob(restored.Where(p => p.Selected), "uninstall"));
    async void Replace_Click(object s, RoutedEventArgs e) => await Guard(() => { var chosen = restored.Where(p => p.Selected).Select(p => p.Copy()).ToList(); foreach (var p in chosen) p.VersionPolicy = "Captured"; return StartJob(chosen, "replace"); });
    async void Settings_Click(object s, RoutedEventArgs e) => await Guard(() =>
    {
        string source = ChocoSourceBox.Text.Trim(); if (source.Length > 0) source = Rules.ValidateSource(source); settings.ChocolateySource = source; settings.AcceptSourceAgreements = AgreementBox.IsChecked == true; settings.CheckAppUpdates = AppUpdateBox.IsChecked == true; Storage.Save(settingsFile, settings); StatusText.Text = "Settings saved. No package managers or system sources were changed."; return Task.CompletedTask;
    });
    async Task CheckAppUpdate()
    {
        AppUpdateStatus.Text = "Checking GitHub releases…"; availableRelease = await appUpdates.Check(CurrentVersion); ApplyUpdateButton.IsEnabled = availableRelease != null;
        AppUpdateStatus.Text = availableRelease == null ? "No newer stable release is available." : "Sterling v" + availableRelease.Version + " is available. Review release notes on GitHub before updating."; if (availableRelease != null) StatusText.Text = "A Sterling update is available. Open Settings & help.";
    }
    async void CheckAppUpdate_Click(object s, RoutedEventArgs e) => await Guard(CheckAppUpdate);
    async void ApplyAppUpdate_Click(object s, RoutedEventArgs e) => await Guard(async () =>
    {
        if (availableRelease == null) return;
        if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("Reopen Sterling without Run as administrator to apply a portable update.");
        AppUpdates.RejectReparseAncestors(AppContext.BaseDirectory); if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "sterling-portable.json"))) throw new InvalidOperationException("Self-update requires the extracted portable release.");
        if (!Confirm("Download Sterling v" + availableRelease.Version + ", verify its checksum, close this application and apply the update? Current binaries will be backed up.")) return;
        string stage = await appUpdates.Stage(availableRelease, Log); var start = new ProcessStartInfo(Path.Combine(stage, "Sterling.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = stage };
        start.ArgumentList.Add(Environment.ProcessId.ToString()); start.ArgumentList.Add(AppContext.BaseDirectory); start.ArgumentList.Add(stage); Process.Start(start); updateClosing = true; Application.Current.Shutdown();
    });
    void OpenLink(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { Log(ex.Message); } }
    void WingetHelp_Click(object s, RoutedEventArgs e) => OpenLink("https://learn.microsoft.com/windows/package-manager/winget/");
    void ChocoHelp_Click(object s, RoutedEventArgs e) => OpenLink("https://docs.chocolatey.org/en-us/guides/organizations/");
    void Releases_Click(object s, RoutedEventArgs e) => OpenLink("https://github.com/" + AppUpdates.Repository + "/releases");
    void Logs_Click(object s, RoutedEventArgs e) => OpenLink(Path.GetDirectoryName(logFile)!);
    void Readme_Click(object s, RoutedEventArgs e) => OpenLink("https://github.com/" + AppUpdates.Repository + "#readme");
    string? ChromeBookmarks()
    {
        if (Process.GetProcessesByName("chrome").Length > 0) throw new InvalidOperationException("Close Chrome, including background processes, first.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data"); var dialog = new OpenFolderDialog { Title = "Select this user's Chrome profile (Default or Profile N)", InitialDirectory = root }; if (dialog.ShowDialog(this) != true) return null;
        string chosen = Path.GetFullPath(dialog.FolderName); if (!string.Equals(Path.GetDirectoryName(chosen), root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Select a profile directly inside the current user's Chrome User Data folder."); AppUpdates.RejectReparseAncestors(chosen); return Path.Combine(chosen, "Bookmarks");
    }
    static void ValidateBookmarks(string path) { if (new FileInfo(path).Length > 50_000_000) throw new InvalidDataException("Bookmarks file exceeds 50 MB."); using var doc = JsonDocument.Parse(File.ReadAllText(path)); if (!doc.RootElement.TryGetProperty("roots", out var roots) || !roots.TryGetProperty("bookmark_bar", out _)) throw new InvalidDataException("Not a Chrome bookmarks file."); }
    async void BackupBookmarks_Click(object s, RoutedEventArgs e) => await Guard(() => { string? source = ChromeBookmarks(); if (source == null) return Task.CompletedTask; ValidateBookmarks(source); var d = new SaveFileDialog { FileName = "Chrome-bookmarks.json", Filter = "Bookmarks JSON|*.json" }; if (d.ShowDialog(this) == true) { File.Copy(source, d.FileName, true); StatusText.Text = "Bookmarks saved. Protect this file: it contains private bookmark titles and URLs."; } return Task.CompletedTask; });
    async void RestoreBookmarks_Click(object s, RoutedEventArgs e) => await Guard(() =>
    {
        string? target = ChromeBookmarks(); if (target == null) return Task.CompletedTask; var source = OpenPath("Chrome bookmarks JSON|*.json|All files|*.*"); if (source == null) return Task.CompletedTask; ValidateBookmarks(source);
        if (!Confirm("Replace bookmarks in " + target + "? The current file will be backed up alongside it. This replaces bookmarks; it does not merge. Chrome Sync may reconcile changes when Chrome opens.")) return Task.CompletedTask;
        if (File.Exists(target)) File.Copy(target, target + ".sterling-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", false); var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.Copy(source, temp); File.Move(temp, target, true); StatusText.Text = "Bookmarks restored for " + Environment.UserName + ". Previous file retained beside Bookmarks."; return Task.CompletedTask;
    });
}
