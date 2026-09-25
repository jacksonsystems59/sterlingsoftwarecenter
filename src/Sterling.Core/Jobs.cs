namespace Sterling.Core;

public sealed class JobItem : Observable
{
    public Package Package { get; set; } = new();
    public string Operation { get; set; } = "install";
    string status = "Queued";
    public string Status { get => status; set { status = value; Changed(); } }
    public string Detail { get; set; } = "";
    public bool RestartRequired { get; set; }
}

public sealed class JobEngine(IEnumerable<IPackageProvider> providers, Action<string> log, string? stateDirectory = null)
{
    readonly string stateDirectory = stateDirectory ?? Storage.Home;
    readonly Dictionary<string, IPackageProvider> providers = providers.ToDictionary(p => p.Name);
    readonly SemaphoreSlim gate = new(1, 1);
    public async Task Run(IList<JobItem> items, CancellationToken stopAfterCurrent = default)
    {
        if (!await gate.WaitAsync(0)) throw new InvalidOperationException("Another job is already running.");
        // Cross-process lock: released by the OS if a process crashes. No privileged service runs here.
        Directory.CreateDirectory(stateDirectory);
        FileStream? machineLock = null;
        try
        {
            machineLock = new FileStream(Path.Combine(stateDirectory, "installer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Storage.Save(Path.Combine(stateDirectory, "last-job.json"), items);
            foreach (var item in items.Where(i => i.Status is "Queued" or "Failed" or "Cancelled").ToList())
            {
                if (stopAfterCurrent.IsCancellationRequested) { item.Status = "Cancelled"; continue; }
                item.Status = "Checking";
                bool commandCompleted = false;
                try
                {
                    Rules.Validate(item.Package);
                    if (item.Operation is not ("install" or "upgrade" or "uninstall" or "replace")) throw new InvalidOperationException("Unsupported job operation.");
                    if (item.Package.Excluded) throw new InvalidOperationException("This package is excluded.");
                    var provider = providers[item.Package.Provider];
                    var installed = await provider.Inventory();
                    var matches = installed.Where(p => p.Id.Equals(item.Package.Id, StringComparison.OrdinalIgnoreCase) && (item.Package.Scope == "unknown" || p.Scope == item.Package.Scope)).ToList();
                    if (matches.Count > 1) throw new InvalidOperationException("Multiple installed instances match; review in the provider before proceeding.");
                    var current = matches.SingleOrDefault();
                    string effectiveOperation = item.Operation;
                    if (item.Operation == "upgrade" && (current == null || !current.Eligible || item.Package.MultipleProviders || item.Package.Pinned))
                        throw new InvalidOperationException("Update held: excluded, pinned, unknown version, ambiguous, or no longer eligible.");
                    if (item.Operation == "install" && current != null)
                    {
                        if (item.Package.VersionPolicy == "Captured" && current.Version != item.Package.Version)
                            throw new InvalidOperationException("A different version is installed. Use explicit Replace; no silent downgrade.");
                        if (item.Package.VersionPolicy == "Newest" && !string.IsNullOrEmpty(current.Available))
                        {
                            if (!current.Eligible || item.Package.MultipleProviders) throw new InvalidOperationException("Existing package is not eligible for automatic upgrade. Review policy first.");
                            effectiveOperation = "upgrade";
                        }
                        else { item.Status = "Skipped"; item.Detail = "Installed version meets the requested policy."; continue; }
                    }
                    if (item.Operation == "uninstall" && current == null) { item.Status = "Skipped"; item.Detail = "Already absent."; continue; }
                    if (item.Operation != "uninstall" && !await provider.Available(item.Package)) throw new InvalidOperationException("Requested package/version is unavailable. Nothing was substituted.");
                    if (item.Operation == "replace")
                    {
                        if (current != null)
                        {
                            item.Status = "Uninstalling";
                            Storage.Save(Path.Combine(stateDirectory, "last-job.json"), items);
                            var removed = await provider.Execute(current, "uninstall", log);
                            item.RestartRequired |= removed.RestartRequired;
                            removed.EnsureSuccess();
                            if ((await provider.Inventory()).Any(p => p.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase) && p.Scope == current.Scope))
                                throw new InvalidOperationException("Uninstall is not verified. Replacement installation was not started.");
                        }
                    }
                    item.Status = "Running";
                    Storage.Save(Path.Combine(stateDirectory, "last-job.json"), items);
                    log($"{item.Operation}: {item.Package.Provider}/{item.Package.Id}");
                    var result = await provider.Execute(item.Package, effectiveOperation == "replace" ? "install" : effectiveOperation, log);
                    item.RestartRequired |= result.RestartRequired;
                    result.EnsureSuccess();
                    commandCompleted = true;
                    item.Status = "Verifying";
                    var after = (await provider.Inventory()).Where(p => p.Id.Equals(item.Package.Id, StringComparison.OrdinalIgnoreCase) && (item.Package.Scope == "unknown" || p.Scope == item.Package.Scope)).ToList();
                    bool verified = item.Operation == "uninstall" ? after.Count == 0 : after.Any(p => item.Package.VersionPolicy != "Captured" || p.Version == item.Package.Version);
                    if (effectiveOperation == "upgrade") verified &= after.All(p => string.IsNullOrEmpty(p.Available));
                    // Do not automatically retry commands that succeeded but cannot yet be verified.
                    item.Status = verified ? "Succeeded" : "Needs review";
                    item.Detail = verified ? "Provider state verified." : "Command succeeded; installed state/version could not be confirmed. Refresh inventory before retrying.";
                    if (item.RestartRequired) item.Detail += " Restart required; no restart requested.";
                }
                catch (Exception ex) { item.Status = commandCompleted ? "Needs review" : "Failed"; item.Detail = ex.Message; log(ex.Message); }
                finally { item.Changed(nameof(item.Detail)); item.Changed(nameof(item.RestartRequired)); Storage.Save(Path.Combine(stateDirectory, "last-job.json"), items); }
            }
        }
        finally { machineLock?.Dispose(); gate.Release(); }
    }
}
