using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Sterling.Core;
namespace Sterling.App;
public partial class MainWindow
{
    List<CommonApplication> commonApps = [];
    List<SecurityFinding> securityFindings = [];
    PackageInformation information = null!;
    TextBlock inventoryScanStatus = null!, sourceStatus = null!, packageDetails = null!, installedDetails = null!;
    Button updateIndicator = null!;
    ListBox suggestions = null!;
    DataGrid commonGrid = null!;
    string browserBackupAttachment = "";
    SourceSnapshot sourceSnapshot = new();
    readonly WindowsTools windowsTools = new();
    bool liveFeatures;
    DispatcherTimer? sourceTimer;
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10), Foreground = new SolidColorBrush(Color.FromRgb(65, 91, 111)) };
    static TextBlock Heading(string text) => new() { Text = text, FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 10) };
    Button ActionButton(string label, Func<Task> action) { var b = new Button { Content = label }; b.Click += async (_, _) => await Guard(action); return b; }
    static DataGrid Table(params (string Header, string Property, bool Edit)[] columns)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, MinHeight = 130, MaxHeight = 330, Margin = new Thickness(0, 4, 0, 10) };
        foreach (var c in columns) grid.Columns.Add(c.Property == "Selected" ? new DataGridCheckBoxColumn { Header = c.Header, Binding = new Binding(c.Property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 55 } : new DataGridTextColumn { Header = c.Header, Binding = new Binding(c.Property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, IsReadOnly = !c.Edit, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        return grid;
    }
    static WrapPanel Buttons(params UIElement[] items) { var panel = new WrapPanel(); foreach (var item in items) panel.Children.Add(item); return panel; }
    void SetupFeatures(bool live)
    {
        liveFeatures = live; information = new(settings, new ProcessRunner());
        string commonPath = Path.Combine(AppContext.BaseDirectory, "common-applications.json");
        if (File.Exists(commonPath)) commonApps = Storage.Load<List<CommonApplication>>(commonPath);
        string advisoryPath = Path.Combine(AppContext.BaseDirectory, "security-findings.json");
        if (File.Exists(advisoryPath)) securityFindings = Storage.Load<List<SecurityFinding>>(advisoryPath);
        SetupFindFeatures(); SetupInventoryFeatures(); SetupCaptureFeatures(); SetupSettingsFeatures(); SetupPrinting();
        var top = (Grid)VersionLabel.Parent; var versionArea = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        top.Children.Remove(VersionLabel); versionArea.Children.Add(VersionLabel);
        updateIndicator = new Button { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromRgb(255, 190, 82)), Margin = new Thickness(14, 0, 0, 0) };
        updateIndicator.Click += (_, _) => { Tabs.SelectedIndex = 4; ReleaseNotesBox.BringIntoView(); };
        versionArea.Children.Add(updateIndicator); Grid.SetColumn(versionArea, 1); top.Children.Add(versionArea);
        Tabs.SelectionChanged += async (_, e) => { if (e.Source == Tabs && Tabs.SelectedIndex == 1 && liveFeatures && !busy) await Guard(RefreshInventory); };
        if (live)
        {
            sourceTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            sourceTimer.Tick += async (_, _) => { if (!busy && (sourceSnapshot.Next(settings.SourceCheckHours) ?? DateTimeOffset.MinValue) <= DateTimeOffset.UtcNow && (sourceSnapshot.AttemptedAt ?? DateTimeOffset.MinValue) < DateTimeOffset.UtcNow.AddMinutes(-15)) await Guard(RefreshSources); };
            sourceTimer.Start(); Closed += (_, _) => sourceTimer.Stop();
        }
    }
    void SetupFindFeatures()
    {
        var grid = (Grid)SearchGrid.Parent; grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var panel = new StackPanel { Width = 300, Margin = new Thickness(14, 0, 0, 0) };
        panel.Children.Add(Note("Verified exact WinGet IDs. Tick multiple applications. Installation still uses the in-app review."));
        commonGrid = Table(("Tick", "Selected", true), ("Common application", "Name", false), ("Installed", "Installed", false)); commonGrid.MaxHeight = 380; commonGrid.ItemsSource = commonApps;
        commonGrid.RowHeight = double.NaN;
        var wrap = new Style(typeof(TextBlock)); wrap.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)); wrap.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(5)));
        foreach (var column in commonGrid.Columns.OfType<DataGridTextColumn>()) column.ElementStyle = wrap;
        foreach (var app in commonApps.Where(a => !a.Enabled)) app.Installed = "Unavailable";
        var unavailable = new Style(typeof(DataGridRow)); var disabled = new DataTrigger { Binding = new Binding("Enabled"), Value = false }; disabled.Setters.Add(new Setter(UIElement.IsEnabledProperty, false)); disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6)); unavailable.Triggers.Add(disabled); commonGrid.RowStyle = unavailable;
        CollectionViewSource.GetDefaultView(commonApps).GroupDescriptions.Add(new PropertyGroupDescription("Group"));
        var groupTitle = new FrameworkElementFactory(typeof(TextBlock)); groupTitle.SetBinding(TextBlock.TextProperty, new Binding("Name")); groupTitle.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); groupTitle.SetValue(TextBlock.MarginProperty, new Thickness(6)); commonGrid.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = groupTitle } });
        commonGrid.SelectionChanged += (_, _) => { if (commonGrid.SelectedItem is CommonApplication c) commonReason.Text = c.Group + " · " + c.Id + "\n" + c.Reason; };
        panel.Children.Add(commonGrid); panel.Children.Add(commonReason);
        panel.Children.Add(ActionButton("Install selected", async () => { commonGrid.CommitEdit(); var chosen = commonApps.Where(a => a.Selected).ToList(); if (chosen.Any(a => !a.Enabled)) throw new InvalidOperationException(string.Join("\n", chosen.Where(a => !a.Enabled).Select(a => a.Reason))); await StartJob(chosen.Select(a => a.Package()), "install"); }));
        panel.Children.Add(ActionButton("Check installed status", RefreshInventory));
        var expander = new Expander { Header = "Common applications", IsExpanded = true, Content = panel, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) }; Grid.SetColumn(expander, 1); Grid.SetRowSpan(expander, 7); grid.Children.Add(expander);
        var searchBar = grid.Children.OfType<DockPanel>().Single(); grid.Children.Remove(searchBar);
        var searchArea = new StackPanel(); searchArea.Children.Add(searchBar);
        suggestions = new ListBox { Visibility = Visibility.Collapsed, MaxHeight = 82, Margin = new Thickness(0, 0, 8, 6) }; searchArea.Children.Add(suggestions); Grid.SetRow(searchArea, 1); grid.Children.Add(searchArea);
        var suggestionData = commonApps.Select(a => a.Name).Concat(["Malwarebytes", "7-Zip", "Adobe Acrobat Reader"]).ToList();
        SearchBox.TextChanged += (_, _) => { string query = SearchBox.Text.Trim(); suggestions.ItemsSource = query.Length < 2 ? Array.Empty<string>() : suggestionData.Where(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(6).ToArray(); suggestions.Visibility = suggestions.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed; };
        void Choose() { if (suggestions.SelectedItem is string term) { SearchBox.Text = term; suggestions.Visibility = Visibility.Collapsed; SearchBox.Focus(); SearchBox.CaretIndex = term.Length; } }
        suggestions.PreviewMouseLeftButtonUp += (_, _) => Choose();
        suggestions.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Choose(); e.Handled = true; } if (e.Key == Key.Escape) { suggestions.Visibility = Visibility.Collapsed; SearchBox.Focus(); e.Handled = true; } };
        SearchBox.PreviewKeyDown += (_, e) => { if (e.Key == Key.Down && suggestions.IsVisible) { suggestions.SelectedIndex = 0; suggestions.Focus(); e.Handled = true; } };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        packageDetails = Note("Select a search result to see its description, publisher, source and version.");
        var scroll = new ScrollViewer { Content = packageDetails, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 6); grid.Children.Add(scroll);
        SearchGrid.SelectionChanged += async (_, _) => { if (SearchGrid.SelectedItem is Package p && liveFeatures) await ShowPackageDetails(p, packageDetails, false); };
    }
    readonly TextBlock commonReason = Note("Browsers · Chrome, Firefox, Brave, Edge\nMedia · VLC\nOffice · enterprise deployment requires verification.");
    void SetupInventoryFeatures()
    {
        var grid = (Grid)InventoryGrid.Parent;
        inventoryScanStatus = Note("Not scanned. Opening this tab starts an asynchronous scan.");
        var topNote = grid.Children.OfType<TextBlock>().First(); grid.Children.Remove(topNote); var intro = new StackPanel(); intro.Children.Add(topNote); intro.Children.Add(inventoryScanStatus); grid.Children.Add(intro);
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); installedDetails = Note("Select an installed application for versions, release notes and verified advisory information.");
        var scroll = new ScrollViewer { Content = installedDetails, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 4); grid.Children.Add(scroll);
        InventoryGrid.SelectionChanged += async (_, _) => { if (InventoryGrid.SelectedItem is Package p && liveFeatures) await ShowPackageDetails(p, installedDetails, true); };
        var style = new Style(typeof(DataGridRow)); var trigger = new DataTrigger { Binding = new Binding(nameof(Package.Eligible)), Value = true }; trigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(222, 246, 228)))); style.Triggers.Add(trigger); InventoryGrid.RowStyle = style;
    }
    async Task ShowPackageDetails(Package p, TextBlock target, bool installed)
    {
        string identity = p.Key; target.Tag = identity; target.Text = "Loading exact package metadata…";
        string text = p.Name + " · " + p.Provider + "/" + p.Id + "\nInstalled/captured: " + p.Version + " · available: " + (string.IsNullOrEmpty(p.Available) ? "not reported" : p.Available) + " · scope: " + p.Scope;
        try
        {
            var copy = p.Copy(); copy.VersionPolicy = "Newest"; var d = await information.Details(copy);
            text += "\nPublisher: " + (string.IsNullOrEmpty(d.Publisher) ? "unavailable" : d.Publisher) + " · source: " + d.Source + " · source version: " + d.Version + "\n" + d.Description;
            if (d.ReleaseNotes.Length > 0) text += "\nVendor/package release notes (not independently classified as security fixes): " + d.ReleaseNotes;
            text += "\nRelease date: " + (d.ReleaseDate.Length > 0 ? d.ReleaseDate : "not supplied") + " · checked " + d.CheckedAt.ToLocalTime().ToString("g");
            if (target.Tag as string != identity) return;
            target.Text = text + (installed ? "\n" + AdvisoryText(p) : "");
            if (PackageInformation.SafeLink(d.Link)) { target.Inlines.Add(new System.Windows.Documents.LineBreak()); var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Vendor / package information")) { NavigateUri = new Uri(d.Link) }; link.RequestNavigate += (_, e) => OpenLink(e.Uri.AbsoluteUri); target.Inlines.Add(link); }
            if (installed) foreach (var finding in securityFindings.Where(f => f.Affects(p))) { target.Inlines.Add(new System.Windows.Documents.LineBreak()); var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(finding.Cve + " · authoritative advisory")); link.Click += (_, _) => OpenLink(finding.Url); target.Inlines.Add(link); }
        }
        catch (Exception ex) { if (target.Tag as string == identity) target.Text = text + "\nMetadata unavailable: " + ex.Message + "\n" + AdvisoryText(p); }
    }
    string AdvisoryText(Package p)
    {
        var hits = securityFindings.Where(f => f.Affects(p)).ToList();
        return hits.Count == 0 ? "No verified security information available. This does not mean the installed version is secure. Coverage is a limited, locally cached advisory set (reviewed 2026-09-25)." : string.Join("\n", hits.Select(f => f.Cve + " · " + f.Title + " · " + f.Edition + " / " + f.Platform + " · published " + f.Published + " · source: " + f.Url));
    }
    void InventoryScanCompleted(List<string> warnings)
    {
        if (inventoryScanStatus == null) return;
        inventoryScanStatus.Text = "Last completed scan: " + DateTime.Now.ToString("g") + " · " + inventory.Count + " rows · " + inventory.Count(p => p.Eligible) + " eligible updates" + (warnings.Count > 0 ? " · INCOMPLETE: " + string.Join(", ", warnings) : "");
        foreach (var a in commonApps) { var found = inventory.Where(p => p.Provider == "winget" && p.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase)).ToList(); a.Installed = !a.Enabled ? "Unavailable" : found.Count > 0 ? string.Join(", ", found.Select(p => p.Version)) : warnings.Contains("winget") ? "Scan failed" : "Not detected"; }
        commonGrid.Items.Refresh();
    }
    async Task DirectUpdates(IEnumerable<Package> selection)
    {
        CommitEdits(); var selected = selection.ToList();
        if (selected.Count == 0) throw new InvalidOperationException("Select an application with an eligible update.");
        jobs.Clear();
        foreach (var p in selected) jobs.Add(new() { Package = p.Copy(), Operation = "upgrade", Status = p.Eligible ? "Queued" : "Skipped", Detail = p.Eligible ? "" : "Not eligible: " + p.State + ". Unknown versions, unavailable updates and uncertain matches are held." });
        Tabs.SelectedIndex = 3; await RunJob();
    }
    async Task BackgroundUpdateCheck() { try { await CheckAppUpdate(); } catch (Exception ex) { AppUpdateStatus.Text = "Update check unavailable: " + ex.Message; Log(ex.Message); } }
    void RefreshUpdateIndicator() { if (updateIndicator == null) return; updateIndicator.Content = availableRelease == null ? "" : "Update to v" + availableRelease.Version; updateIndicator.Visibility = availableRelease == null ? Visibility.Collapsed : Visibility.Visible; }
    async Task ClassifyCapture()
    {
        CommitEdits(); foreach (var p in restored)
        {
            StatusText.Text = "Checking restore readiness: " + p.Name;
            try { var d = p.Manageable ? await information.Details(p) : null; bool available = p.Manageable && await Provider(p.Provider).Available(p); p.Disposition = PackageInformation.Categorize(p, d, available, commonApps); }
            catch (Exception ex) { p.Disposition = "Manual: check failed — " + ex.Message; }
            p.Changed(nameof(p.Disposition));
        }
        RefreshCaptureGroups(); StatusText.Text = "Readiness checked. Manual items remain in the saved bundle and report.";
    }
    async Task RefreshSources()
    {
        sourceSnapshot.AttemptedAt = DateTimeOffset.UtcNow; sourceStatus.Text = "Refreshing source metadata (tracked IDs only)…";
        List<string> errors = []; var next = new Dictionary<string, PackageDetails>(sourceSnapshot.Packages); var absent = new HashSet<string>();
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); var r = await new ProcessRunner().Run(new(WingetProvider.Executable, ["source", "update", "--name", "winget", "--disable-interactivity"]), Log, timeout.Token); r.EnsureSuccess(); } catch (Exception ex) { errors.Add("WinGet source refresh: " + ex.Message); }
        var tracked = commonApps.Where(a => a.Enabled).Select(a => a.Package()).Concat(inventory.Where(p => p.Manageable)).DistinctBy(p => p.Provider + ":" + p.Id).Take(150);
        foreach (var p in tracked)
        {
            try { var copy = p.Copy(); copy.VersionPolicy = "Newest"; string key = p.Provider + ":" + p.Id; next[key] = await information.Details(copy, true); }
            catch (KeyNotFoundException) { absent.Add(p.Provider + ":" + p.Id); next.Remove(p.Provider + ":" + p.Id); }
            catch (Exception ex) { errors.Add(p.Id + ": " + ex.Message); }
        }
        if (!string.IsNullOrEmpty(settings.ChocolateySource)) { try { await Provider("chocolatey").Search("chocolatey"); } catch (Exception ex) { errors.Add("Configured Chocolatey source: " + ex.Message); } }
        sourceSnapshot.Changes = SourceSnapshot.Compare(sourceSnapshot.Packages, next, absent); sourceSnapshot.Packages = next; sourceSnapshot.Error = string.Join("\n", errors);
        if (errors.Count == 0) sourceSnapshot.CompletedAt = DateTimeOffset.UtcNow;
        Storage.Save(Path.Combine(Storage.Home, "source-snapshot.json"), sourceSnapshot); ShowSourceStatus();
    }
    void ShowSourceStatus() => sourceStatus.Text = "Scope: up to 150 tracked installed/common package IDs, not a full catalogue.\nLast complete: " + (sourceSnapshot.CompletedAt?.ToLocalTime().ToString("g") ?? "never") + " · next due: " + (sourceSnapshot.Next(settings.SourceCheckHours)?.ToLocalTime().ToString("g") ?? "now") + "\nLast attempted: " + (sourceSnapshot.AttemptedAt?.ToLocalTime().ToString("g") ?? "never") + "\n" + (string.IsNullOrEmpty(settings.ChocolateySource) ? "Chocolatey source not configured." : "Chocolatey source: " + settings.ChocolateySource) + "\n" + sourceSnapshot.Error + "\n" + string.Join("\n", sourceSnapshot.Changes);
}
