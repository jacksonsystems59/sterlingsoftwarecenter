using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Sterling.Core;
namespace Sterling.App;
public partial class MainWindow
{
    List<BrowserProfile> browserProfiles = [];
    BrowserManifest? browserManifest;
    string browserOpenedRoot = "";
    DataGrid browserGrid = null!;
    ComboBox browserUser = null!, browserSource = null!, browserTarget = null!;
    CheckBox browserRestoreBookmarks = null!, browserRestoreSettings = null!, browserInstall = null!;
    TextBlock browserStatus = null!;
    WrapPanel olderChoices = null!;
    bool olderAcknowledged = true;
    void SetupCaptureFeatures()
    {
        var tab = (TabItem)Tabs.Items[2]; var software = (UIElement)tab.Content; tab.Content = null;
        var inner = new TabControl(); inner.Items.Add(new TabItem { Header = "Captured software", Content = software }); tab.Content = inner;
        var view = CollectionViewSource.GetDefaultView(restored); view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Package.Disposition), new CaptureCategoryConverter()));
        var factory = new FrameworkElementFactory(typeof(TextBlock)); factory.SetBinding(TextBlock.TextProperty, new Binding("Name")); factory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); factory.SetValue(TextBlock.MarginProperty, new Thickness(8, 12, 8, 8));
        RestoreGrid.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = factory } });
        restored.CollectionChanged += (_, _) => RefreshCaptureGroups();
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(Heading("Browser Backup")); body.Children.Add(Note("Select the Windows user and detected browser profiles. Close the browsers first. Passwords, cookies, sessions and authentication are excluded. Extensions are inventory only. Choose external or network storage that survives reinstalling Windows."));
        browserUser = new ComboBox { MinWidth = 440 };
        var roots = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
            if (key != null) foreach (var name in key.GetSubKeyNames()) { using var profile = key.OpenSubKey(name); if (profile?.GetValue("ProfileImagePath") is string path && Directory.Exists(Environment.ExpandEnvironmentVariables(path)) && (name.StartsWith("S-1-5-21", StringComparison.Ordinal) || name.StartsWith("S-1-12-1", StringComparison.Ordinal))) roots.Add(Environment.ExpandEnvironmentVariables(path)); }
        browserUser.ItemsSource = roots.Distinct().ToList(); browserUser.SelectedIndex = 0;
        body.Children.Add(Buttons(browserUser, ActionButton("Detect browser profiles", DetectBrowserProfiles)));
        browserGrid = Table(("Tick", "Selected", true), ("Browser", "Browser", false), ("Profile", "Profile", false), ("Windows user", "User", false));
        foreach (var name in new[] { "Bookmarks", "Settings", "Extensions" }) browserGrid.Columns.Add(new DataGridCheckBoxColumn { Header = name, Binding = new Binding(name) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        body.Children.Add(browserGrid);
        browserStatus = Note("Detection uses browser profile folders for the selected Windows user. Unsupported/locked data is reported per profile."); body.Children.Add(browserStatus);
        browserGrid.SelectionChanged += (_, _) => { if (browserGrid.SelectedItem is BrowserProfile p) browserStatus.Text = p.Browser + " · " + p.Path + "\n" + p.Status; };
        body.Children.Add(Buttons(ActionButton("Backup Browser Data…", BackupBrowsers), ActionButton("Open browser backup…", OpenBrowserBackup), ActionButton("Open attached capture backup", async () => { if (string.IsNullOrEmpty(browserBackupAttachment)) throw new InvalidOperationException("No browser backup attached to the current capture."); await LoadBrowserBackup(browserBackupAttachment); })));
        body.Children.Add(Heading("Restore to a selected user profile"));
        body.Children.Add(Note("Install the browser first if needed, open it once as the target user to create a profile, then close it and detect again. Pick a matching target below. Data restore always has an in-app review; Firefox bookmarks use the browser's supported manual restore chooser."));
        browserSource = new ComboBox { DisplayMemberPath = "Profile", MinWidth = 250 }; browserTarget = new ComboBox { DisplayMemberPath = "Path", MinWidth = 440 };
        browserSource.SelectionChanged += (_, _) => { if (browserSource.SelectedItem is BrowserProfile source) browserTarget.ItemsSource = browserProfiles.Where(p => p.Browser == source.Browser).ToList(); };
        body.Children.Add(Note("Saved profile:")); body.Children.Add(browserSource); body.Children.Add(Note("Target profile for the selected Windows user:")); body.Children.Add(browserTarget);
        browserRestoreBookmarks = new CheckBox { Content = "Restore bookmarks (Firefox: manual import)", Margin = new Thickness(0, 4, 0, 6) };
        browserRestoreSettings = new CheckBox { Content = "Restore supported settings only", Margin = new Thickness(0, 4, 0, 6) };
        browserInstall = new CheckBox { Content = "Install/update the browser first using the package job system", Margin = new Thickness(0, 4, 0, 6) };
        body.Children.Add(browserRestoreBookmarks); body.Children.Add(browserRestoreSettings); body.Children.Add(browserInstall); body.Children.Add(ActionButton("Review browser restore", PrepareBrowserRestore));
        inner.Items.Add(new TabItem { Header = "Browser Backup", Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var review = (Grid)ReviewGrid.Parent;
        olderChoices = Buttons(ActionButton("Install newest", async () => { foreach (var job in pendingJobs) job.Package.VersionPolicy = "Newest"; olderAcknowledged = true; await PrepareReview(pendingJobs, "Review newest-version restore"); }), ActionButton("Continue with captured version", () => { olderAcknowledged = true; olderChoices.Visibility = Visibility.Collapsed; ReviewConsent_Changed(this, new RoutedEventArgs()); return Task.CompletedTask; }), ActionButton("Cancel", () => { ReviewCancel_Click(this, new RoutedEventArgs()); return Task.CompletedTask; }));
        olderChoices.Visibility = Visibility.Collapsed;
        var oldTop = review.Children.OfType<StackPanel>().First(); oldTop.Children.Add(olderChoices);
    }
    void RefreshCaptureGroups() { var view = CollectionViewSource.GetDefaultView(restored); if (view is IEditableCollectionView edit && (edit.IsEditingItem || edit.IsAddingNew)) return; view.Refresh(); }
    Task DetectBrowserProfiles()
    {
        string user = browserUser.SelectedItem as string ?? throw new InvalidOperationException("Select the intended Windows user.");
        browserProfiles = BrowserBackups.Detect(user); browserGrid.ItemsSource = browserProfiles;
        browserTarget.ItemsSource = browserSource.SelectedItem is BrowserProfile source ? browserProfiles.Where(p => p.Browser == source.Browser).ToList() : browserProfiles;
        browserStatus.Text = browserProfiles.Count + " profiles detected for " + user + ". Access is limited to readable profiles; do not use another user's credentials for their browser session."; return Task.CompletedTask;
    }
    async Task BackupBrowsers()
    {
        browserGrid.CommitEdit(DataGridEditingUnit.Cell, true); browserGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var dialog = new SaveFileDialog { FileName = "BrowserBackup.zip", Filter = "Browser backup|*.zip", Title = "Choose external/network storage that will survive the Windows reinstall" };
        if (dialog.ShowDialog(this) != true) return;
        var manifest = await BrowserBackups.Backup(browserProfiles, dialog.FileName); browserBackupAttachment = dialog.FileName;
        browserStatus.Text = "Saved " + dialog.FileName + "\n" + string.Join("\n", manifest.Profiles.Select(p => p.Browser + " / " + p.User + " / " + p.Profile + ": " + p.Status)) + "\nThis backup will be included when saving the software capture. " + StorageLocationWarning(dialog.FileName);
    }
    static string StorageLocationWarning(string path) => string.Equals(Path.GetPathRoot(path), Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase) ? "WARNING: destination is on the Windows volume; copy it to protected external storage before reinstalling." : "Confirm this location will remain available after reinstalling Windows.";
    async Task OpenBrowserBackup() { string? path = OpenPath("Browser backup|*.zip"); if (path != null) await LoadBrowserBackup(path); }
    async Task LoadBrowserBackup(string path)
    {
        browserOpenedRoot = await IntegrityBundle.Open(path); browserManifest = Storage.Load<BrowserManifest>(Path.Combine(browserOpenedRoot, "manifest.json"));
        if (browserManifest.SchemaVersion != 1 || browserManifest.Profiles.Count > 1000) throw new InvalidDataException("Unsupported browser backup.");
        browserSource.ItemsSource = browserManifest.Profiles; browserSource.SelectedIndex = 0; browserRestoreBookmarks.IsChecked = false; browserRestoreSettings.IsChecked = false; browserInstall.IsChecked = false;
        browserStatus.Text = "Integrity verified. " + string.Join("\n", browserManifest.Profiles.Select(p => p.Browser + " / " + p.User + " / " + p.Profile + ": " + p.Status));
    }
    Task PrepareBrowserRestore()
    {
        if (browserSource.SelectedItem is not BrowserProfile source) throw new InvalidOperationException("Open a browser backup and select its saved profile.");
        if (browserTarget.SelectedItem is not BrowserProfile target) throw new InvalidOperationException("Select an existing target profile for the same browser. Install/open the browser first if the target profile does not exist.");
        bool bookmarks = browserRestoreBookmarks.IsChecked == true, settingsData = browserRestoreSettings.IsChecked == true, install = browserInstall.IsChecked == true;
        if (!bookmarks && !settingsData) throw new InvalidOperationException("Select at least one supported data type.");
        var package = new Package { Id = source.PackageId, Name = source.Browser, Scope = "machine" };
        reviewRows = [new() { Item = new() { Package = package }, Action = install ? "Install then browser restore" : "Browser restore", Proposed = (bookmarks ? "Bookmarks " : "") + (settingsData ? "settings" : ""), Scope = target.User, Notes = "Target: " + target.Path + ". " + source.Status + " Existing target files are backed up. Close all browser processes. No secrets, sessions or extension binaries are restored. Firefox bookmark import requires a manual step." }];
        pendingJobs = []; pendingSpecial = async () =>
        {
            if (install) { jobs.Clear(); jobs.Add(new() { Package = package }); Tabs.SelectedIndex = 3; await RunJob(); if (jobs.Any(j => j.Status is not ("Succeeded" or "Skipped"))) throw new InvalidOperationException("Browser installation did not succeed; data was not restored."); }
            browserStatus.Text = await BrowserBackups.Restore(browserOpenedRoot, source, target, bookmarks, settingsData); Tabs.SelectedIndex = 2;
        };
        ShowReview("Review browser data restore", "Selected user: " + target.User); return Task.CompletedTask;
    }
}
public sealed class CaptureCategoryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value as string == "Ready for automatic install" ? "Ready for automatic install" : "Manual attention required — check readiness to verify exact version and unattended method";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

