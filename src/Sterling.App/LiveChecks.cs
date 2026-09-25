using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sterling.Core;
namespace Sterling.App;
public partial class MainWindow
{
    internal async Task RunLiveChecks(string directory)
    {
        Directory.CreateDirectory(directory); sourceTimer?.Stop(); List<string> checks = [];
        async Task Settle() { while (busy) await Task.Delay(100); await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
        void Assert(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); checks.Add(text); }
        await InitializeAsync(_ => { });
        Tabs.SelectedIndex = 1; await Settle();
        Assert(inventory.Count > 0 && inventoryScanStatus.Text.Contains("Last completed"), "Entering Installed & Updates automatically scans live inventory and records completion");
        var first = inventory.FirstOrDefault(p => p.Id == "Mozilla.Firefox") ?? inventory.First(p => p.Manageable);
        InventoryGrid.SelectedItem = first; await ShowPackageDetails(first, installedDetails, true); await Settle();
        Assert(installedDetails.Text.Contains("source version") || installedDetails.Text.Contains("No verified security information"), "Installed application details and honest advisory state are shown");
        App.Render(this, Path.Combine(directory, "installed.png"));
        Tabs.SelectedIndex = 0; SearchBox.Text = "fire";
        Assert(suggestions.Items.Count > 0, "Live suggestion list reacts to typing without a job");
        SearchBox.Text = "Mozilla.Firefox"; await Search();
        var result = ((IEnumerable<Package>)SearchGrid.ItemsSource).First(p => p.Id == "Mozilla.Firefox"); SearchGrid.SelectedItem = result;
        await ShowPackageDetails(result, packageDetails, false); await Settle();
        Assert(packageDetails.Text.Contains("Mozilla") && packageDetails.Text.Contains("source version"), "Live exact WinGet search provides publisher/version/description details");
        App.Render(this, Path.Combine(directory, "search-details.png"));
        Tabs.SelectedIndex = 2; var inner = (TabControl)((TabItem)Tabs.Items[2]).Content; inner.SelectedIndex = 1; await DetectBrowserProfiles(); await Settle();
        App.Render(this, Path.Combine(directory, "browser-backup.png"));
        Assert(browserProfiles.All(p => p.User == browserUser.SelectedItem as string), "Browser detection uses the selected Windows user");
        Tabs.SelectedIndex = 6; await CapturePrinters(); await Settle();
        Assert(printerItems.Count > 0, "Real printer inventory is displayed in Printing"); App.Render(this, Path.Combine(directory, "printing.png"));
        Tabs.SelectedIndex = 4; await Settle(); App.Render(this, Path.Combine(directory, "settings.png"));
        Storage.Save(Path.Combine(directory, "live-checks.json"), new { Passed = checks.Count, Checks = checks, InventoryRows = inventory.Count, EligibleUpdates = inventory.Count(p => p.Eligible), Printers = printerItems.Count });
    }
}
