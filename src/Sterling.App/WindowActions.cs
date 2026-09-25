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
    readonly BookmarkRecipe recipe;
    readonly string settingsFile = Path.Combine(Storage.Home, "settings.json");
    readonly string logFile = Path.Combine(Storage.Home, "logs", DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
    readonly AppUpdates appUpdates = new();
    AppRelease? availableRelease;
    CancellationTokenSource? jobStop;
    bool busy, updateClosing;
    public MainWindow() : this(null, null, null, null) { }
    public MainWindow(Settings? testSettings, List<IPackageProvider>? testProviders, BookmarkRecipe? testRecipe, string? testState)
    {
        InitializeComponent();
        ReviewGrid.RowHeight = double.NaN;
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        try { settings = testSettings ?? (File.Exists(settingsFile) ? Storage.Load<Settings>(settingsFile) : new()); }
        catch (Exception ex) { settings = new(); Log("Settings could not be loaded: " + ex.Message); }
        providers = testProviders ?? [new WingetProvider(new ProcessRunner(), settings), new ChocolateyProvider(new ProcessRunner(), settings)];
        recipe = testRecipe ?? new BookmarkRecipe(ChromeRoot, () => Process.GetProcessesByName("chrome").Length > 0);
        engine = new(providers, Log, testState, recipe);
        BasketGrid.ItemsSource = basket; InventoryGrid.ItemsSource = inventory; RestoreGrid.ItemsSource = restored; JobsGrid.ItemsSource = jobs;
        basket.CollectionChanged += (_, _) => { if (!busy) StatusText.Text = basket.Count + " packages selected for deployment"; };
        BasketScope.ItemsSource = RestoreScope.ItemsSource = new[] { "unknown", "user", "machine" };
        BasketPolicy.ItemsSource = RestorePolicy.ItemsSource = new[] { "Newest", "Captured" };
        DetailPolicy.ItemsSource = new[] { "Newest", "Captured" };
        DataProfileBox.ItemsSource = new[] { "Default" }.Concat(Directory.Exists(ChromeRoot) ? Directory.GetDirectories(ChromeRoot).Select(Path.GetFileName).Where(n => n != null && System.Text.RegularExpressions.Regex.IsMatch(n, @"^Profile [0-9]+$"))! : []).Distinct().ToList();
        ChocoSourceBox.Text = settings.ChocolateySource; AgreementBox.IsChecked = settings.AcceptSourceAgreements; AppUpdateBox.IsChecked = settings.CheckAppUpdates;
        VersionLabel.Text = "v" + CurrentVersion.ToString(3) + "  ·  WINDOWS x64";
        Subtitle.Text = Environment.MachineName + "  /  " + Environment.UserName + "  /  LOCAL WORKSPACE";
        SetupFeatures(testProviders == null);
        Closing += (_, e) => { if (busy && !updateClosing) { e.Cancel = true; StatusText.Text = "Operation running. Use Stop after current package before closing."; } };
    }
    public async Task InitializeAsync(Action<string> status)
    {
        status("Detecting package providers…"); await Detect();
        status("Loading previous job and local policy…"); LoadPreviousJob();
        if (settings.CheckAppUpdates) { status("Checking Sterling releases in the background…"); _ = BackgroundUpdateCheck(); }
        status("Your workspace is ready");
    }
    static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version!;
    async Task Guard(Func<Task> action)
    {
        if (busy) { StatusText.Text = "An operation is already running. See Jobs & logs."; return; }
        busy = true;
        try { await action(); } catch (Exception ex) { StatusText.Text = ex.Message; Log(ex.ToString()); } finally { busy = false; ReviewConsent_Changed(this, new RoutedEventArgs()); }
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
    Task StartJob(IEnumerable<Package> packages, string operation) { CommitEdits(); return PrepareReview(ReviewBuilder.Jobs(packages, operation)); }
    async Task RunJob()
    {
        foreach (var item in jobs) item.PropertyChanged += (_, _) => UpdateJobProgress();
        UpdateJobProgress();
        jobStop?.Dispose(); jobStop = new(); StatusText.Text = "Running sequential package job. Other packages continue after a failure.";
        await engine.Run(jobs, jobStop.Token);
        UpdateJobProgress();
        StatusText.Text = $"Job finished · {jobs.Count(j => j.Status == "Succeeded")} succeeded · {jobs.Count(j => j.Status == "Skipped")} skipped · {jobs.Count(j => j.Status == "Failed")} failed · {jobs.Count(j => j.Status == "Needs review")} need review" + (jobs.Any(j => j.RestartRequired) ? " · Restart required" : "");
    }
    async void Install_Click(object s, RoutedEventArgs e) => await Guard(() => StartJob(basket, "install"));
    async void Retry_Click(object s, RoutedEventArgs e) => await Guard(PrepareRetry);
    void Stop_Click(object s, RoutedEventArgs e) { jobStop?.Cancel(); StatusText.Text = "Stop requested. The current installer will finish; remaining packages will be cancelled."; }
    async Task RefreshInventory()
    {
        var selectedKeys = inventory.Where(p => p.Selected).Select(p => p.Key).ToHashSet(); inventory.Clear(); if (inventoryScanStatus != null) inventoryScanStatus.Text = "Scanning installed applications and updates…"; StatusText.Text = "Reading installed applications and applicable updates…"; List<string> warnings = [];
        foreach (var p in providers) try { foreach (var item in await p.Inventory()) inventory.Add(item); } catch (Exception ex) { warnings.Add(p.Name); Log("Inventory incomplete for " + p.Name + ": " + ex.Message); }
        foreach (var item in ReadRegistry()) if (!inventory.Any(p => p.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase) && p.Version == item.Version && p.Scope == item.Scope)) inventory.Add(item);
        static string Normal(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        foreach (var group in inventory.Where(p => p.Manageable).GroupBy(p => Normal(p.Name))) if (group.Select(p => p.Provider).Distinct().Count() > 1) foreach (var p in group) p.MultipleProviders = true;
        foreach (var p in inventory) { p.Excluded = settings.Exclusions.Contains(p.Provider + ":" + p.Id); p.Selected = selectedKeys.Contains(p.Key); }
        StatusText.Text = $"{inventory.Count} inventory rows · {inventory.Count(p => p.Eligible)} eligible updates" + (warnings.Count > 0 ? " · Provider unavailable/incomplete: " + string.Join(", ", warnings) + ". See logs." : "");
        InventoryScanCompleted(warnings);
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
    async void UpdateSelected_Click(object s, RoutedEventArgs e) => await Guard(() => DirectUpdates(inventory.Where(p => p.Selected)));
    async void UpdateAll_Click(object s, RoutedEventArgs e) => await Guard(async () => { await RefreshInventory(); await DirectUpdates(inventory.Where(p => p.Eligible)); });
    async void Uninstall_Click(object s, RoutedEventArgs e) => await Guard(() => StartJob(inventory.Where(p => p.Selected), "uninstall"));
    void Exclude_Click(object s, RoutedEventArgs e) => SetExclusions(true);
    void Unexclude_Click(object s, RoutedEventArgs e) => SetExclusions(false);
    void SetExclusions(bool exclude)
    {
        if (busy) return;
        foreach (var p in inventory.Where(p => p.Selected && p.Manageable)) { string key = p.Provider + ":" + p.Id; if (exclude) settings.Exclusions.Add(key); else settings.Exclusions.Remove(key); p.Excluded = exclude; p.Changed(nameof(p.State)); }
        Storage.Save(settingsFile, settings); StatusText.Text = "Exclusion policy saved.";
    }
    async void Capture_Click(object s, RoutedEventArgs e) => await Guard(CaptureForReview);
    string? SavePath(string filename) { var d = new SaveFileDialog { Filter = "Sterling bundle (*.sterling.json)|*.sterling.json|JSON (*.json)|*.json", FileName = filename, AddExtension = true }; return d.ShowDialog(this) == true ? d.FileName : null; }
    string? OpenPath(string filter) { var d = new OpenFileDialog { Filter = filter }; return d.ShowDialog(this) == true ? d.FileName : null; }
    async void SaveCapture_Click(object s, RoutedEventArgs e) => await Guard(() =>
    {
        CommitEdits(); var items = (Tabs.SelectedIndex == 2 ? restored : inventory).Select(p => p.Copy()).ToList(); if (items.Count == 0) throw new InvalidOperationException("Capture or scan the PC before saving its inventory.");
        var path = SavePath(Environment.MachineName + "-capture.sterling.json"); if (path == null) return Task.CompletedTask;
        foreach (var p in items) { p.Selected = false; p.VersionPolicy = "Captured"; if (!Rules.KnownVersion(p.Version)) p.Disposition = "Captured version unknown — review policy"; }
        Storage.SaveBundle(path, new Bundle { Name = Environment.MachineName + " capture", Packages = items, BrowserBackupFile = browserBackupAttachment }); StatusText.Text = "Capture saved: " + path + (string.Equals(Path.GetPathRoot(path), Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase) ? " · WARNING: on the Windows volume; copy the JSON and its data folder to external storage before erasing Windows." : " · Keep the JSON and any data folder together."); return Task.CompletedTask;
    });
    async void SaveList_Click(object s, RoutedEventArgs e) => await Guard(() =>
    {
        CommitEdits(); if (basket.Count == 0) throw new InvalidOperationException("Add packages before saving a deployment list."); foreach (var p in basket) Rules.Validate(p);
        var path = SavePath("Standard Workstation.sterling.json"); if (path != null) { Storage.SaveBundle(path, new Bundle { Kind = "list", ExplicitPresetOptions = true, Name = Path.GetFileNameWithoutExtension(path), Packages = basket.Select(p => p.Copy()).ToList() }); StatusText.Text = "Deployment list saved: " + path; } return Task.CompletedTask;
    });
    async void OpenBundle_Click(object s, RoutedEventArgs e) => await Guard(() =>
    {
        var path = OpenPath("Sterling / JSON bundles|*.json"); if (path != null) LoadCapture(path); return Task.CompletedTask;
    });
    void Preset_Click(object s, RoutedEventArgs e)
    {
        if (busy) return; restored.Clear(); foreach (var (id, name) in new[] { ("Google.Chrome", "Google Chrome"), ("7zip.7zip", "7-Zip"), ("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader") }) restored.Add(new Package { Id = id, Name = name, Scope = "machine", Selected = true }); BundleLabel.Text = "Standard Workstation · editable starter list";
    }
    void SelectRestore_Click(object s, RoutedEventArgs e) { if (!busy) foreach (var p in restored) p.Selected = p.Disposition == "Ready for automatic install" && !p.Excluded; }
    void CapturedPolicy_Click(object s, RoutedEventArgs e) => SetPolicy("Captured");
    void NewestPolicy_Click(object s, RoutedEventArgs e) => SetPolicy("Newest");
    void SetPolicy(string policy) { if (busy) return; CommitEdits(); foreach (var p in restored) { p.VersionPolicy = policy; p.Changed(nameof(p.VersionPolicy)); } }
    async void Availability_Click(object s, RoutedEventArgs e) => await Guard(async () =>
    {
        await ClassifyCapture();
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
        AppUpdateStatus.Text = "Checking GitHub releases…"; availableRelease = await appUpdates.Check(CurrentVersion); ApplyUpdateButton.IsEnabled = availableRelease != null; RefreshUpdateIndicator();
        AppUpdateStatus.Text = availableRelease == null ? "No newer stable release is available." : "Sterling v" + availableRelease.Version + " is available. Review the release notes below before updating."; ReleaseNotesBox.Text = availableRelease?.Notes ?? "No newer stable release. The current portable version is up to date."; if (availableRelease != null) StatusText.Text = "A Sterling update is available. Open Settings & help.";
    }
    async void CheckAppUpdate_Click(object s, RoutedEventArgs e) => await Guard(CheckAppUpdate);
    async void ApplyAppUpdate_Click(object s, RoutedEventArgs e) => await Guard(PrepareAppUpdateReview);
    void OpenLink(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { Log(ex.Message); } }
    void WingetHelp_Click(object s, RoutedEventArgs e) => OpenLink("https://learn.microsoft.com/windows/package-manager/winget/");
    void ChocoHelp_Click(object s, RoutedEventArgs e) => OpenLink("https://docs.chocolatey.org/en-us/guides/organizations/");
    void Releases_Click(object s, RoutedEventArgs e) => OpenLink("https://github.com/" + AppUpdates.Repository + "/releases");
    void Logs_Click(object s, RoutedEventArgs e) => OpenLink(Path.GetDirectoryName(logFile)!);
    void Readme_Click(object s, RoutedEventArgs e) => OpenLink("https://github.com/" + AppUpdates.Repository + "#readme");
}
