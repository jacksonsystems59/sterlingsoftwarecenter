using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Sterling.Core;
namespace Sterling.App;
public partial class MainWindow
{
    TextBox maintenanceLog = null!;
    TextBlock maintenanceStatus = null!, printingStatus = null!, chocolateyStatus = null!;
    DataGrid printerGrid = null!;
    List<PrinterItem> printerItems = [];
    JsonElement printerSnapshot;
    string printerBundleRoot = "";
    CheckBox printerDefault = null!, printerPreferences = null!;
    static string CurrentSid => WindowsIdentity.GetCurrent().User!.Value;
    void MaintenanceLog(string message) { Dispatcher.InvokeAsync(() => { maintenanceLog.AppendText(message + Environment.NewLine); maintenanceLog.ScrollToEnd(); maintenanceStatus.Text = message.Split('\n').FirstOrDefault() ?? "Running"; }); Log(message); }
    void SetupSettingsFeatures()
    {
        var tab = (TabItem)Tabs.Items[4]; var panel = (StackPanel)((ScrollViewer)tab.Content).Content;
        var choco = new StackPanel(); chocolateyStatus = Note(File.Exists(ChocolateyProvider.Executable) ? "Chocolatey detected. Configure an authorised internal source below." : "Chocolatey is not installed. Setup requires administrator approval and your organisation's approved Chocolatey installer package.");
        choco.Children.Add(chocolateyStatus);
        choco.Children.Add(Note("Install/Configure uses a locally selected Chocolatey nupkg with an engineer-verified SHA-256. Setup changes ProgramData/PATH and disables the default Community source. Sterling does not use the public repository as its MSP backend. A proxy alone does not establish licensing permission."));
        var hash = new TextBox { ToolTip = "Expected SHA-256 from the approved distribution source", MinWidth = 500 }; choco.Children.Add(Note("Expected installer SHA-256 (from your approved distribution source):")); choco.Children.Add(hash);
        choco.Children.Add(Buttons(ActionButton("Install/Configure Chocolatey…", () =>
        {
            if (File.Exists(ChocolateyProvider.Executable)) { ChocoSourceBox.Focus(); ChocoSourceBox.BringIntoView(); chocolateyStatus.Text = "Chocolatey already installed. Enter the approved internal/customer source below and Save settings; it will be reused for searches."; return Task.CompletedTask; }
            string? file = OpenPath("Approved Chocolatey installer|*.nupkg"); if (file == null) return Task.CompletedTask;
            string expected = hash.Text.Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(expected, "^[a-fA-F0-9]{64}$")) throw new InvalidOperationException("Enter the expected SHA-256 before setup.");
            return PrepareToolReview("Install Chocolatey", "Runs the selected administrator-approved package with UAC. Modifies ProgramData/PATH, then disables the default Community source. No application packages are installed. Source: " + file, async () => { await AppUpdates.Verify(file, expected); var result = await windowsTools.Run("InstallChocolatey", new { Package = file, Sha256 = expected }, true, MaintenanceLog); chocolateyStatus.Text = result.GetProperty("Status").GetString(); await Detect(); Tabs.SelectedIndex = 4; });
        }), ActionButton("Repository terms / setup guidance", () => { OpenLink("https://docs.chocolatey.org/en-us/choco/setup/"); return Task.CompletedTask; })));
        panel.Children.Insert(0, new Expander { Header = "Chocolatey status and setup", IsExpanded = true, Content = choco });
        var sources = new StackPanel(); sourceStatus = Note("");
        try { string file = Path.Combine(Storage.Home, "source-snapshot.json"); if (File.Exists(file)) sourceSnapshot = Storage.Load<SourceSnapshot>(file); } catch (Exception ex) { sourceSnapshot.Error = ex.Message; }
        ShowSourceStatus(); sources.Children.Add(sourceStatus);
        var interval = new ComboBox { ItemsSource = new[] { 1, 6, 12, 24, 48, 168 }, SelectedItem = settings.SourceCheckHours, MinWidth = 90 };
        sources.Children.Add(Buttons(Note("Check interval (hours):"), interval, ActionButton("Save interval", () => { settings.SourceCheckHours = interval.SelectedItem is int h ? h : 24; Storage.Save(settingsFile, settings); ShowSourceStatus(); return Task.CompletedTask; }), ActionButton("Refresh sources now", RefreshSources)));
        panel.Children.Add(new Expander { Header = "Sources and tracked package changes", Content = sources, IsExpanded = false });
        var health = new StackPanel(); health.Children.Add(Note("Windows Health runs one operation at a time with normal UAC. RestoreHealth and SFC can repair/change Windows; RestoreHealth may download an online repair source. Full sequence: CheckHealth → ScanHealth → RestoreHealth if corruption is reported (or explicitly requested) → SFC. No automatic reboot."));
        var forceRepair = new CheckBox { Content = "Full sequence: explicitly run RestoreHealth even if no corruption is reported" }; health.Children.Add(forceRepair);
        var buttons = new WrapPanel(); foreach (string step in new[] { "CheckHealth", "ScanHealth", "RestoreHealth", "SFC", "Full" })
            buttons.Children.Add(ActionButton(step == "Full" ? "Run full health check/repair" : step == "SFC" ? "sfc /scannow" : "DISM " + step, () => PrepareToolReview("Windows Health: " + step, "Elevation required. " + (step is "RestoreHealth" or "SFC" or "Full" ? "This can repair Windows files; an online source may be needed." : "This checks Windows image health.") + " No automatic reboot.", async () =>
            {
                Tabs.SelectedIndex = 4; maintenanceLog.Clear();
                var result = await windowsTools.Run("Health", new { Step = step, AlwaysRepair = forceRepair.IsChecked == true }, true, MaintenanceLog);
                var summaries = result.GetProperty("Results").EnumerateArray().Select(r => r.GetProperty("Skipped").GetBoolean() ? r.GetProperty("Step").GetString() + ": Skipped — " + r.GetProperty("Output").GetString() : FormatHealth(r)).ToList();
                maintenanceStatus.Text = string.Join("\n", summaries); maintenanceLog.AppendText("\nRESULTS\n" + maintenanceStatus.Text);
            })));
        health.Children.Add(buttons); maintenanceStatus = Note("No Windows Health operation has run."); health.Children.Add(maintenanceStatus);
        maintenanceLog = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12 };
        health.Children.Add(maintenanceLog); health.Children.Add(ActionButton("Export maintenance log…", () => { var d = new SaveFileDialog { FileName = "Sterling-Windows-Health.log", Filter = "Log|*.log" }; if (d.ShowDialog(this) == true) File.WriteAllText(d.FileName, maintenanceLog.Text); return Task.CompletedTask; }));
        panel.Children.Add(new Expander { Header = "Windows Health", Content = health, IsExpanded = true });
    }
    static string FormatHealth(JsonElement row) { string step = row.GetProperty("Step").GetString()!; var result = HealthInterpretation.Interpret(step, row.GetProperty("Code").GetInt32(), row.GetProperty("Output").GetString()!); return step + ": " + result.State + " — " + result.Detail; }
    Task PrepareToolReview(string title, string notes, Func<Task> run)
    {
        reviewRows = [new() { Item = new() { Package = new() { Name = title, Id = "Windows.Tool", Provider = "Windows" } }, Action = title, Proposed = "Explicit engineer action", Scope = "This Windows PC", Notes = notes }];
        pendingJobs = []; pendingSpecial = run; ShowReview(title, "Review the Windows changes and choose Start."); return Task.CompletedTask;
    }
    void SetupPrinting()
    {
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(Heading("Other → Printing")); body.Children.Add(Note("Manage the Windows console, capture printers before reinstalling Windows, or restore a reviewed backup. Backups include queue/port metadata, supported settings and applicable third-party printer driver exports. User preferences are best-effort and driver-specific."));
        printingStatus = Note("Detect Print Management and capture printer inventory to begin."); body.Children.Add(printingStatus);
        body.Children.Add(Buttons(ActionButton("Detect Print Management", async () => { var r = await windowsTools.Run("PrintConsole", new { }, false, MaintenanceLog); printingStatus.Text = r.GetProperty("Status").GetString(); }), ActionButton("Open Print Management", () =>
        { string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "printmanagement.msc"); if (!File.Exists(path)) throw new InvalidOperationException("Print Management is absent. Use Install/Enable to check this Windows capability."); Process.Start(new ProcessStartInfo("mmc.exe") { UseShellExecute = true, ArgumentList = { path } }); return Task.CompletedTask; }), ActionButton("Install/Enable Print Management", () => PrepareToolReview("Enable Print Management", "Uses the Print.Management.Console capability discovered on this Windows image. Requires UAC and may need Windows Update or an organisational source. No automatic reboot.", async () => { var r = await windowsTools.Run("EnablePrintConsole", new { }, true, MaintenanceLog); printingStatus.Text = r.ToString(); Tabs.SelectedIndex = 6; }))));
        body.Children.Add(Heading("Printer & Driver Backup"));
        body.Children.Add(Note("Selected Windows user: " + WindowsIdentity.GetCurrent().Name + ". Run as the intended user. A different UAC identity cannot restore this user's default/preferences. WSD/IPP/vendor monitors may require rediscovery; shared connections need the print server. PrinterBackup.zip is unencrypted: protect addresses, queue names and preferences."));
        body.Children.Add(Buttons(ActionButton("Capture printers", CapturePrinters), ActionButton("Select all", () => { foreach (var p in printerItems) p.Selected = true; printerGrid.Items.Refresh(); return Task.CompletedTask; }), ActionButton("Backup printers and drivers…", BackupPrinters), ActionButton("Open PrinterBackup.zip…", OpenPrinters)));
        printerGrid = Table(("Tick", "Selected", true), ("Printer", "Name", false), ("Driver / architecture", "DriverName", false), ("Version", "DriverVersion", false), ("Port", "PortName", false), ("Address", "HostAddress", false), ("Restore readiness", "Status", false));
        printerGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Conflict decision", ItemsSource = new[] { "Skip existing", "Replace queue" }, SelectedItemBinding = new Binding("Conflict") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 145 }); body.Children.Add(printerGrid);
        printerDefault = new CheckBox { Content = "Restore the backed-up default printer for this Windows user (only restored queues)", Margin = new Thickness(0, 6, 0, 6) };
        printerPreferences = new CheckBox { Content = "Attempt captured per-user preferences with the matching driver; manual verification may remain", Margin = new Thickness(0, 6, 0, 10) };
        body.Children.Add(printerDefault); body.Children.Add(printerPreferences); body.Children.Add(ActionButton("Review selected printer restore", ReviewPrinters));
        body.Children.Add(Note("Existing queues default to Skip. Replace queue is explicit and may lose its current settings; drivers and ports with conflicts are never overwritten automatically. The human-readable Inventory.txt in the ZIP reports partial capture failures. Windows Health / maintenance logs include the detailed command output."));
        Tabs.Items.Add(new TabItem { Header = "Other · Printing", Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
    }
    async Task CapturePrinters()
    {
        printingStatus.Text = "Scanning queues, ports, drivers, default printer and supported settings…";
        printerSnapshot = await windowsTools.Run("PrintInventory", new { }, false, MaintenanceLog);
        printerItems = JsonSerializer.Deserialize<List<PrinterItem>>(printerSnapshot.GetProperty("Printers").GetRawText(), Storage.Json)!;
        printerBundleRoot = ""; printerGrid.ItemsSource = printerItems;
        printingStatus.Text = printerItems.Count + " queues captured for " + printerSnapshot.GetProperty("User").GetString() + ". Default: " + (printerItems.FirstOrDefault(p => p.Default)?.Name ?? "not detected") + ". Tick the queues to back up.";
    }
    Task BackupPrinters()
    {
        printerGrid.CommitEdit(DataGridEditingUnit.Cell, true); printerGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var names = printerItems.Where(p => p.Selected).Select(p => p.Name).ToList(); if (names.Count == 0) throw new InvalidOperationException("Capture printers and tick the queues to back up.");
        var dialog = new SaveFileDialog { FileName = "PrinterBackup.zip", Filter = "Printer backup|*.zip", Title = "Choose external/network storage that survives Windows reinstall" }; if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        return PrepareToolReview("Backup printers and drivers", "Export selected queues: " + string.Join(", ", names) + ". Destination: " + dialog.FileName + ". UAC is used for supported driver-store export. No installed printers are changed. " + StorageLocationWarning(dialog.FileName), async () =>
        {
            string root = Path.Combine(Storage.Home, "printer-backups", Guid.NewGuid().ToString("N"));
            var result = await windowsTools.Run("PrintBackup", new { Root = root, Names = names, UserSid = CurrentSid }, true, MaintenanceLog);
            await Task.Run(() => IntegrityBundle.Pack(root, dialog.FileName));
            printingStatus.Text = "Saved " + dialog.FileName + "\n" + string.Join("\n", result.GetProperty("Printers").EnumerateArray().Select(p => p.GetProperty("Name").GetString() + ": " + p.GetProperty("Status").GetString())) + "\n" + StorageLocationWarning(dialog.FileName); Tabs.SelectedIndex = 6;
        });
    }
    async Task OpenPrinters()
    {
        string? path = OpenPath("PrinterBackup.zip|*.zip"); if (path == null) return;
        printerBundleRoot = await IntegrityBundle.Open(path); printerSnapshot = Storage.Load<JsonElement>(Path.Combine(printerBundleRoot, "manifest.json"));
        if (printerSnapshot.GetProperty("SchemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported printer manifest.");
        string compatibility = PrinterPlanning.Validate(printerSnapshot.GetProperty("Architecture").GetString()!, printerSnapshot.GetProperty("WindowsVersion").GetString()!, Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")!, Environment.OSVersion.Version.ToString());
        printerItems = JsonSerializer.Deserialize<List<PrinterItem>>(printerSnapshot.GetProperty("Printers").GetRawText(), Storage.Json)!;
        printerGrid.ItemsSource = printerItems; printerDefault.IsChecked = false; printerPreferences.IsChecked = false;
        printingStatus.Text = "Bundle integrity verified. " + compatibility + "\nCaptured user: " + printerSnapshot.GetProperty("User").GetString() + ". Restore user: " + WindowsIdentity.GetCurrent().Name + ". Select queues and review conflicts.";
    }
    async Task ReviewPrinters()
    {
        if (string.IsNullOrEmpty(printerBundleRoot)) throw new InvalidOperationException("Open a PrinterBackup.zip before restoring.");
        printerGrid.CommitEdit(DataGridEditingUnit.Cell, true); printerGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = printerItems.Where(p => p.Selected).ToList(); if (selected.Count == 0) throw new InvalidOperationException("Tick at least one printer.");
        var current = await windowsTools.Run("PrintInventory", new { }, false, MaintenanceLog); var names = current.GetProperty("Printers").EnumerateArray().Select(p => p.GetProperty("Name").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool restoreDefault = printerDefault.IsChecked == true, preferences = printerPreferences.IsChecked == true;
        reviewRows = selected.Select(p => new ReviewRow { Item = new() { Package = new() { Name = p.Name, Id = p.DriverName, Provider = "Windows printing" } }, Action = "Restore printer", Installed = names.Contains(p.Name) ? "Existing queue" : "Absent", Proposed = p.Conflict, Scope = WindowsIdentity.GetCurrent().Name, Notes = PrinterPlanning.Plan(p, names.Contains(p.Name)) + " Driver: " + p.DriverName + " " + p.DriverVersion + " / " + p.Architecture + ". Port: " + p.PortName + " " + p.HostAddress + ". " + p.Status + (restoreDefault && p.Default ? " Restore user default." : "") + (preferences ? " Attempt per-user preferences." : "") }).ToList();
        var choices = selected.Select(p => new { p.Name, p.Conflict }).ToList(); pendingJobs = [];
        pendingSpecial = async () => { var result = await windowsTools.Run("PrintRestore", new { Root = printerBundleRoot, Printers = choices, UserSid = CurrentSid, Default = restoreDefault, Preferences = preferences }, true, MaintenanceLog); printingStatus.Text = string.Join("\n", result.GetProperty("Results").EnumerateArray().Select(p => p.GetProperty("Name").GetString() + ": " + p.GetProperty("Status").GetString() + " — " + p.GetProperty("Detail").GetString())); Tabs.SelectedIndex = 6; };
        ShowReview("Review printer restore", "Review every printer and conflict decision. UAC may appear. No automatic reboot.");
    }
}
