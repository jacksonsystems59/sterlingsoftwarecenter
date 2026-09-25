using System.Diagnostics;
using System.Text.Json;
namespace Sterling.Core;
public record HealthResult(string State, string Detail);
public static class HealthInterpretation
{
    public static HealthResult Interpret(string step, int exitCode, string output)
    {
        string s = output.Replace("\0", "").ToLowerInvariant();
        if (exitCode != 0 && exitCode != 3010) return new("Failed", $"Exit 0x{exitCode:X8}. Review the actual output and DISM/CBS logs.");
        if (s.Contains("cannot be repaired") || s.Contains("unable to fix") || s.Contains("could not perform")) return new("Further action", "Corruption or repair failure remains; inspect the Windows servicing logs.");
        if (s.Contains("successfully repaired") || s.Contains("restore operation completed successfully")) return new("Repaired", exitCode == 3010 ? "Windows requests a restart; no reboot was initiated." : "Repair completed; review the output.");
        if (s.Contains("component store is repairable")) return new("Corruption found", "RestoreHealth is required before the SFC stage.");
        if (s.Contains("no component store corruption detected") || s.Contains("did not find any integrity violations")) return new("Healthy", "Windows reported no corruption for this check.");
        return new("Further action", "Command exited successfully, but its health result could not be verified (possibly localised output). Read the output; this is not a Passed result.");
    }
}
public sealed class WindowsTools
{
    static readonly SemaphoreSlim Gate = new(1, 1);
    public async Task<JsonElement> Run(string operation, object request, bool elevated, Action<string> log)
    {
        if (!await Gate.WaitAsync(0)) throw new InvalidOperationException("Another Windows operation is running.");
        try
        {
            string root = Path.Combine(Storage.Home, "windows-jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string input = Path.Combine(root, "request.json"), result = Path.Combine(root, "result.json"), output = Path.Combine(root, "output.log");
            Storage.Save(input, request);
            string script = Path.Combine(AppContext.BaseDirectory, "Tools", "WindowsTools.ps1");
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = elevated, CreateNoWindow = !elevated, WindowStyle = ProcessWindowStyle.Hidden };
            if (elevated) info.Verb = "runas";
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Operation", operation, "-RequestPath", input, "-ResultPath", result, "-LogPath", output }) info.ArgumentList.Add(a);
            log("Starting " + operation + (elevated ? " with Windows UAC." : ".") + " Log: " + output);
            using var process = Process.Start(info) ?? throw new IOException("Windows operation did not start.");
            int consumed = 0;
            while (!process.HasExited)
            {
                await Task.Delay(500);
                if (File.Exists(output)) { try { using var stream = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var reader = new StreamReader(stream); string all = reader.ReadToEnd(); if (all.Length > consumed) { log(all[consumed..]); consumed = all.Length; } } catch (IOException) { } }
            }
            if (!File.Exists(result)) throw new IOException("Windows operation produced no result. Exit: " + process.ExitCode + ". Log: " + output);
            var answer = Storage.Load<JsonElement>(result);
            if (answer.TryGetProperty("Error", out var error)) throw new IOException(error.GetString());
            return answer;
        }
        finally { Gate.Release(); }
    }
}
public sealed class PrinterItem
{
    public bool Selected { get; set; }
    public string Name { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string PortName { get; set; } = "";
    public string HostAddress { get; set; } = "";
    public string PortKind { get; set; } = "";
    public string Connection { get; set; } = "";
    public bool Default { get; set; }
    public string Status { get; set; } = "";
    public string Conflict { get; set; } = "Skip existing";
}
public static class PrinterPlanning
{
    public static string Validate(string architecture, string osVersion, string currentArchitecture, string currentOs)
    {
        if (architecture != currentArchitecture) throw new InvalidDataException("Printer driver architecture differs. Restore on the matching architecture.");
        if (!Version.TryParse(osVersion, out var old) || !Version.TryParse(currentOs, out var now) || old.Major != now.Major) throw new InvalidDataException("Unsupported Windows version mismatch.");
        return old.Build == now.Build ? "Windows build matches." : "Windows build differs. Drivers/settings may be incompatible; verify each printer result.";
    }
    public static string Plan(PrinterItem item, bool exists) => exists && item.Conflict == "Skip existing" ? "Skip existing queue; no settings or default changes." : item.PortKind is "TCP/IP" or "Local" || item.Connection.StartsWith(@"\\") ? "Driver → port → queue/connection → supported settings → selected user's default/preferences." : "Manual attention: WSD/IPP/vendor monitor cannot be recreated by this recipe. Open Print Management or the vendor installer.";
}
