using System.Diagnostics;
using System.Security.Principal;
using Sterling.Core;
string logPath = Path.Combine(Storage.Home, "updater.log");
Directory.CreateDirectory(Storage.Home);
try
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        throw new InvalidOperationException("Run portable updates without elevation. Protected installations require an administrator-managed deployment.");
    if (args.Length != 3 || !int.TryParse(args[0], out int pid)) throw new ArgumentException("Usage: Sterling.Updater <parentPid> <targetFolder> <stageFolder>");
    string target = Path.GetFullPath(args[1]), stage = Path.GetFullPath(args[2]);
    if (!string.Equals(Path.TrimEndingDirectorySeparator(stage), Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Helper must run from the verified staging directory.");
    try
    {
        using var parent = Process.GetProcessById(pid);
        if (!string.Equals(parent.MainModule?.FileName, Path.Combine(target, "Sterling.App.exe"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Parent application path does not match target.");
        if (!parent.WaitForExit(60000)) throw new TimeoutException("Close Sterling Software Centre before applying the update.");
    }
    catch (ArgumentException) { }
    string backup = Path.Combine(Storage.Home, "update-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
    AppUpdates.Apply(stage, target, backup);
    File.AppendAllText(logPath, $"{DateTimeOffset.Now:u} Update applied. Previous files: {backup}\n");
    Process.Start(new ProcessStartInfo(Path.Combine(target, "Sterling.App.exe")) { UseShellExecute = true, WorkingDirectory = target });
    return 0;
}
catch (Exception ex)
{
    File.AppendAllText(logPath, $"{DateTimeOffset.Now:u} {ex}\n");
    Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true, ArgumentList = { logPath } });
    return 1;
}
