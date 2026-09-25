namespace Sterling.Core;

public sealed class ReviewRow
{
    public JobItem Item { get; init; } = new();
    public string Application => Item.Package.Name;
    public string PackageId => Item.Package.Id;
    public string Provider => Item.Package.Provider;
    public string Action { get; init; } = "";
    public string Installed { get; init; } = "Not detected";
    public string Proposed { get; init; } = "";
    public string Scope { get; init; } = "";
    public string Notes { get; init; } = "";
    public bool Blocked { get; init; }
}

public static class ReviewBuilder
{
    public static List<JobItem> Jobs(IEnumerable<Package> selection, string operation)
    {
        List<JobItem> result = [];
        foreach (var selected in selection.DistinctBy(p => p.Key))
        {
            var p = selected.Copy();
            result.Add(new() { Package = p, Operation = operation });
            if (p.Data.RestoreBookmarks && operation is "install" or "replace")
                result.Add(new() { Package = p.Copy(), Operation = "data-restore", DependsOnIndex = result.Count - 1 });
        }
        return result;
    }
    public static ReviewRow Describe(JobItem item, IReadOnlyList<Package> installed, Settings settings, IDataRecipeRunner recipes, string? inventoryError = null)
    {
        var p = item.Package;
        var matches = installed.Where(x => x.Provider == p.Provider && x.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase) && (p.Scope == "unknown" || x.Scope == p.Scope)).ToList();
        var current = matches.FirstOrDefault();
        bool blocked = false;
        var notes = new List<string>();
        try { Rules.Validate(p, item.Operation is not ("uninstall" or "data-restore")); } catch (Exception ex) { blocked = true; notes.Add(ex.Message); }
        if (p.Excluded || settings.Exclusions.Contains(p.Provider + ":" + p.Id)) { blocked = true; notes.Add("Excluded by policy."); }
        if (matches.Count > 1) { blocked = true; notes.Add("Multiple installed instances; manual review required."); }
        if (item.Operation == "upgrade" && (current?.Eligible != true || p.Pinned || p.MultipleProviders)) { blocked = true; notes.Add("Update is not eligible: check pin, version, exclusion or provider overlap."); }
        if (item.Operation == "data-restore")
        {
            try { if (!BookmarkRecipe.Supports(p)) throw new InvalidDataException("No supported data recipe."); BookmarkRecipe.ValidateFile(p.Data.BookmarksFile); if (p.Data.Sha256.Length != 64) throw new InvalidDataException("Choose a bookmark backup to record its checksum first."); AppUpdates.Verify(p.Data.BookmarksFile, p.Data.Sha256).GetAwaiter().GetResult(); notes.Add(recipes.Describe(p)); }
            catch (Exception ex) { blocked = true; notes.Add(ex.Message); }
        }
        else
        {
            if (inventoryError != null) { blocked = true; notes.Add("Installed-state check failed: " + inventoryError); }
            notes.Add(p.State + ". " + (p.Scope == "machine" || p.Provider == "chocolatey" ? "Machine change; administrator elevation may be required." : p.Scope == "user" ? "Uses the current Windows user's session." : "Provider default scope; may require elevation."));
            if (item.Operation is "uninstall" or "replace") notes.Add("Uninstall may affect settings, data and licence activation. Replacement is not an automatic rollback.");
            else notes.Add("Starting accepts the selected package licence agreements.");
            notes.Add("Restart requirement may be reported by the installer; Sterling will not request a restart.");
        }
        string action = item.Operation switch { "install" => p.VersionPolicy == "Captured" ? "Restore / install" : "Install / update", "upgrade" => "Update", "replace" => "Replace with captured", "uninstall" => "Uninstall", "data-restore" => "Restore bookmarks", _ => item.Operation };
        return new() { Item = item, Action = action, Installed = current?.Version ?? "Not detected", Proposed = item.Operation == "uninstall" ? "Remove" : item.Operation == "data-restore" ? "Selected backup" : p.VersionPolicy == "Captured" ? p.Version : !string.IsNullOrEmpty(current?.Available) ? current.Available : "Newest available (checked at start)", Scope = item.Operation == "data-restore" ? "Current user / " + p.Data.ChromeProfile : p.Scope, Notes = string.Join(" ", notes), Blocked = blocked };
    }
}
