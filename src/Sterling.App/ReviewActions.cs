using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sterling.Core;
namespace Sterling.App;

public partial class MainWindow
{
    List<JobItem> pendingJobs = [];
    List<ReviewRow> reviewRows = [];
    Func<Task>? pendingSpecial;
    int reviewReturnTab;
    static string ChromeRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data");

    async Task PrepareReview(List<JobItem> proposed, string? title = null)
    {
        CommitEdits();
        if (proposed.Count == 0) throw new InvalidOperationException("Tick one or more applications first.");
        pendingSpecial = null;
        pendingJobs = proposed;
        reviewRows = [];
        var installed = new List<Package>();
        var errors = new Dictionary<string, string>();
        StatusText.Text = "Preparing review; no package changes are being made…";
        foreach (string name in proposed.Where(j => j.Status is "Queued" or "Failed").Select(j => j.Package.Provider).Distinct())
        {
            try { installed.AddRange(await Provider(name).Inventory()); }
            catch (Exception ex) { errors[name] = ex.Message; }
        }
        foreach (var job in proposed.Where(j => j.Status is "Queued" or "Failed"))
        {
            job.Package.Excluded |= settings.Exclusions.Contains(job.Package.Provider + ":" + job.Package.Id);
            reviewRows.Add(ReviewBuilder.Describe(job, installed, settings, recipe, errors.GetValueOrDefault(job.Package.Provider)));
        }
        ShowReview(title ?? "Review your changes", $"{reviewRows.Count} ordered step(s) across {reviewRows.Select(r => r.Item.Package.Key).Distinct().Count()} application(s). Every affected application is listed below.");
        if (liveFeatures)
        {
            List<string> older = [];
            foreach (var job in proposed.Where(j => j.Operation is "install" or "replace" && j.Package.VersionPolicy == "Captured"))
            {
                try
                {
                    var newest = job.Package.Copy(); newest.VersionPolicy = "Newest"; var latest = await information.Details(newest);
                    if (Version.TryParse(job.Package.Version, out var captured) && Version.TryParse(latest.Version, out var available) && captured < available)
                        older.Add(job.Package.Name + ": captured " + captured + " is older than " + available + ". " + AdvisoryText(job.Package));
                }
                catch (Exception ex) { Log("Latest-version comparison unavailable: " + ex.Message); }
            }
            if (older.Count > 0) { olderAcknowledged = false; olderChoices.Visibility = Visibility.Visible; ReviewWarning.Text += "\n" + string.Join("\n", older); ReviewStartButton.IsEnabled = false; }
        }
    }
    void ShowReview(string title, string summary)
    {
        olderAcknowledged = true; if (olderChoices != null) olderChoices.Visibility = Visibility.Collapsed;
        reviewReturnTab = Tabs.SelectedIndex == 5 ? reviewReturnTab : Tabs.SelectedIndex;
        ReviewTitle.Text = title; ReviewSummary.Text = summary; ReviewGrid.ItemsSource = reviewRows;
        ReviewWarning.Text = reviewRows.Any(r => r.Blocked) ? "One or more items are blocked. Read their details below, then go Back to revise the selection or policy. Start is disabled." : "No changes have been made. Start runs these steps in order. UAC may appear for installers; Sterling will not request a PC restart.";
        ReviewConsent.IsChecked = false; ReviewStartButton.IsEnabled = false;
        Tabs.SelectedIndex = 5; StatusText.Text = "Review ready · Back / Cancel makes no changes";
    }
    void ReviewConsent_Changed(object sender, RoutedEventArgs e)
    {
        if (ReviewStartButton != null) ReviewStartButton.IsEnabled = ReviewConsent.IsChecked == true && reviewRows.Count > 0 && !reviewRows.Any(r => r.Blocked) && !busy && olderAcknowledged;
    }
    void ReviewCancel_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        pendingJobs = []; pendingSpecial = null; reviewRows = []; ReviewGrid.ItemsSource = null;
        ReviewConsent.IsChecked = false; ReviewStartButton.IsEnabled = false;
        ReviewTitle.Text = "Review cancelled"; ReviewSummary.Text = "No package or application-data changes were made.";
        Tabs.SelectedIndex = reviewReturnTab; StatusText.Text = "Review cancelled · no changes made";
    }
    async void ReviewStart_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (ReviewConsent.IsChecked != true || reviewRows.Count == 0 || reviewRows.Any(r => r.Blocked) || !olderAcknowledged) throw new InvalidOperationException("Review all items and acknowledge the terms and older-version choice before starting.");
        ReviewStartButton.IsEnabled = false;
        var special = pendingSpecial; pendingSpecial = null;
        if (special != null) { reviewRows = []; ReviewGrid.ItemsSource = null; await special(); return; }
        var launch = pendingJobs; pendingJobs = []; reviewRows = []; ReviewGrid.ItemsSource = null;
        jobs.Clear(); foreach (var item in launch) jobs.Add(item);
        Tabs.SelectedIndex = 3; await RunJob();
    });
    async Task PrepareRetry()
    {
        if (!jobs.Any(j => j.Status == "Failed")) throw new InvalidOperationException("No failed steps to retry.");
        var copied = JsonSerializer.Deserialize<List<JobItem>>(JsonSerializer.Serialize(jobs))!;
        foreach (var item in copied)
        {
            if (item.Status == "Cancelled") item.Status = "Not retried";
            item.Package.Excluded = settings.Exclusions.Contains(item.Package.Provider + ":" + item.Package.Id);
        }
        await PrepareReview(copied, "Review failed-step retry");
    }
    void UpdateJobProgress()
    {
        int done = jobs.Count(j => j.Status is "Succeeded" or "Skipped" or "Failed" or "Needs review" or "Cancelled" or "Not retried");
        JobProgress.Value = jobs.Count == 0 ? 0 : 100.0 * done / jobs.Count;
        JobProgressLabel.Text = $"{done} / {jobs.Count} steps complete";
    }
    async Task CaptureForReview()
    {
        await RefreshInventory(); restored.Clear();
        foreach (var p in inventory) { var copy = p.Copy(); copy.Selected = true; copy.VersionPolicy = "Captured"; restored.Add(copy); }
        BundleLabel.Text = Environment.MachineName + " · capture review";
        Tabs.SelectedIndex = 2; RestoreGrid.SelectedItem = restored.FirstOrDefault(BookmarkRecipe.Supports) ?? restored.FirstOrDefault();
        StatusText.Text = "Untick excluded software; select an app for data backup options, then Save capture. Keep the JSON and its data folder together.";
    }
    void LoadCapture(string path)
    {
        var bundle = Storage.LoadBundle(path); restored.Clear();
        browserBackupAttachment = bundle.BrowserBackupFile;
        foreach (var p in bundle.Packages) { p.Excluded |= settings.Exclusions.Contains(p.Provider + ":" + p.Id); restored.Add(p); }
        BundleLabel.Text = bundle.Name + " · " + bundle.CapturedAt.ToLocalTime().ToString("g");
        RestoreGrid.SelectedItem = restored.FirstOrDefault();
        StatusText.Text = $"{restored.Count} items loaded; review and tick items to restore. Data steps from captures start unchecked.";
    }
    async void ReviewRestore_Click(object sender, RoutedEventArgs e) => await Guard(() =>
    {
        if (liveFeatures && restored.Any(p => p.Selected && p.Disposition != "Ready for automatic install")) throw new InvalidOperationException("Check availability/readiness, then select the automatic items. Manual items remain in the capture and need their suggested action.");
        return PrepareReview(ReviewBuilder.Jobs(restored.Where(p => p.Selected), "install"), "Review bulk restore");
    });
    void RestoreSelection_Changed(object sender, SelectionChangedEventArgs e) => ShowAppDetails();
    void Profile_Changed(object sender, SelectionChangedEventArgs e) => ShowDataLocations();
    void ShowAppDetails()
    {
        var p = RestoreGrid.SelectedItem as Package;
        DetailPolicy.DataContext = p; DataRecipePanel.DataContext = p;
        DataAppTitle.Text = p?.Name ?? "Select an application for its details";
        DataAppSummary.Text = p == null ? "Choose a captured application above. Optional data steps start unchecked." : $"{p.Provider} / {p.Id}\nCaptured: {p.Version} · scope: {p.Scope}\n{p.Disposition}";
        bool supported = p != null && BookmarkRecipe.Supports(p);
        DataRecipePanel.IsEnabled = supported;
        DataRecipePanel.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        DetailPolicy.IsEnabled = p?.Manageable == true;
        DataRecipeStatus.Text = supported ? "Available recipe: Chrome bookmarks. Back up or choose a file, then explicitly select whether to restore it. Registry and shortcut recipes are not supported." : "No tested application-data recipe exists for this application. Back up settings using the vendor's documented method.";
        ShowDataLocations();
    }
    void ShowDataLocations()
    {
        if (DataLocations == null) return;
        if (RestoreGrid.SelectedItem is not Package p || !BookmarkRecipe.Supports(p)) { DataLocations.Text = ""; return; }
        try { DataLocations.Text = "Target: " + recipe.Target(p.Data) + "\nBackup: " + (string.IsNullOrEmpty(p.Data.BookmarksFile) ? "none selected" : p.Data.BookmarksFile) + "\nReplaces bookmarks after retaining the previous file. No passwords or cookies. Close Chrome first."; }
        catch (Exception ex) { DataLocations.Text = ex.Message; }
    }
    async void BackupBookmarks_Click(object sender, RoutedEventArgs e) => await Guard(() =>
    {
        if (RestoreGrid.SelectedItem is not Package p || !BookmarkRecipe.Supports(p)) throw new InvalidOperationException("Select a supported Chrome application.");
        var dialog = new SaveFileDialog { FileName = "Chrome-" + p.Data.ChromeProfile + "-bookmarks.json", Filter = "Bookmarks JSON|*.json" };
        if (dialog.ShowDialog(this) == true) { recipe.Backup(p.Data, dialog.FileName); ShowDataLocations(); StatusText.Text = "Bookmark backup attached. Restore remains an explicit choice. Protect this file: it contains private titles and URLs."; }
        return Task.CompletedTask;
    });
    async void ChooseBookmarks_Click(object sender, RoutedEventArgs e) => await Guard(() =>
    {
        if (RestoreGrid.SelectedItem is not Package p || !BookmarkRecipe.Supports(p)) throw new InvalidOperationException("Select a supported Chrome application.");
        var path = OpenPath("Chrome bookmark JSON|*.json|All files|*.*");
        if (path != null) { BookmarkRecipe.ValidateFile(path); p.Data.BookmarksFile = path; p.Data.Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); ShowDataLocations(); }
        return Task.CompletedTask;
    });
    async void ReviewData_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (RestoreGrid.SelectedItem is not Package p || !p.Data.RestoreBookmarks) throw new InvalidOperationException("Select Chrome and tick its bookmark restoration option first.");
        await PrepareReview([new JobItem { Package = p.Copy(), Operation = "data-restore" }], "Review application-data restore");
    });
    Task PrepareAppUpdateReview()
    {
        if (availableRelease == null) throw new InvalidOperationException("Check for a newer Sterling version first.");
        var release = availableRelease;
        reviewRows = [new() { Item = new JobItem { Package = new Package { Name = "Sterling Software Centre", Id = "Sterling.SoftwareCentre", Provider = "GitHub" } }, Action = "Application update", Installed = CurrentVersion.ToString(3), Proposed = release.Version, Scope = "Portable folder", Notes = "Downloads " + release.Filename + ". Verifies SHA-256, closes Sterling, backs up replaced binaries, preserves other files and user data, then restarts and verifies the displayed version. No Windows service is installed or changed." }];
        pendingJobs = [];
        pendingSpecial = async () =>
        {
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("Reopen without administrator elevation to apply the portable update.");
            AppUpdates.RejectReparseAncestors(AppContext.BaseDirectory);
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "sterling-portable.json"))) throw new InvalidOperationException("Self-update requires the extracted portable release.");
            string probe = Path.Combine(AppContext.BaseDirectory, ".sterling-write-" + Guid.NewGuid().ToString("N")); File.WriteAllText(probe, ""); File.Delete(probe);
            Tabs.SelectedIndex = 4; AppUpdateStatus.Text = "Downloading and verifying Sterling v" + release.Version + "…";
            string stage = await appUpdates.Stage(release, Log);
            var start = new ProcessStartInfo(Path.Combine(stage, "Sterling.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = stage };
            start.ArgumentList.Add(Environment.ProcessId.ToString()); start.ArgumentList.Add(AppContext.BaseDirectory); start.ArgumentList.Add(stage);
            Process.Start(start); updateClosing = true; Application.Current.Shutdown();
        };
        ShowReview("Review Sterling application update", "Release notes are available in Settings & help. Your approval applies only to this application update.");
        return Task.CompletedTask;
    }
    public void VerifyUpdatedVersion(string expected, string receipt)
    {
        string allowed = Path.Combine(Storage.Home, "updates", "receipts") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(receipt).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid update receipt location.");
        bool verified = CurrentVersion.ToString(3) == expected && VersionLabel.Text.StartsWith("v" + expected + " ", StringComparison.Ordinal);
        Storage.Save(receipt, new { Success = verified, Version = CurrentVersion.ToString(3), Displayed = VersionLabel.Text });
        AppUpdateStatus.Text = verified ? "Update completed and displayed version verified: v" + expected : "Update version verification failed; see updater.log and retained backups.";
        StatusText.Text = AppUpdateStatus.Text;
    }
}
