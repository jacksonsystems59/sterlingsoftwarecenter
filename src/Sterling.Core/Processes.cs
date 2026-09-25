using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Sterling.Core;

public record Command(string File, IReadOnlyList<string> Args);
public record ProcessResult(int Code, string Output)
{
    public bool Success => Code is 0 or 3010 or 1641 || unchecked((uint)Code) == 0x8A150109;
    public bool RestartRequired => Code is 3010 or 1641 || unchecked((uint)Code) is 0x8A150109 or 0x8A15010A or 0x8A15010B;
    public void EnsureSuccess() { if (!Success) throw new InvalidOperationException($"Command failed (0x{Code:X8}). {Output}"); }
}
public interface ICommandRunner { Task<ProcessResult> Run(Command command, Action<string>? log = null, CancellationToken token = default); }
public sealed partial class ProcessRunner : ICommandRunner
{
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")] private static partial Regex Ansi();
    public static string Clean(string s) => Ansi().Replace(s, "");
    public async Task<ProcessResult> Run(Command command, Action<string>? log = null, CancellationToken token = default)
    {
        var info = new ProcessStartInfo(command.File) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        foreach (var arg in command.Args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        process.Start();
        var buffer = new StringBuilder();
        async Task Read(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                line = Clean(line);
                lock (buffer) { if (buffer.Length < 8_000_000) buffer.AppendLine(line); }
                log?.Invoke(line);
            }
        }
        var readers = Task.WhenAll(Read(process.StandardOutput), Read(process.StandardError));
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } await readers; throw; }
        await readers;
        return new(process.ExitCode, buffer.ToString());
    }
}

public static class WingetTable
{
    // Refuse unknown/localised layouts; never guess column meanings.
    public static List<Dictionary<string, string>> Parse(string output)
    {
        var lines = ProcessRunner.Clean(output).Replace("\r", "").Split('\n');
        int separator = Array.FindIndex(lines, l => l.Trim().Length >= 5 && l.Trim().All(c => c == '-'));
        if (separator < 1) throw new InvalidDataException("WinGet did not return a recognised English table. See the operation log; check prerequisites, source agreements and Windows display language.");
        var header = lines[separator - 1];
        var columns = Regex.Matches(header, @"\S+(?: \S+)?(?=\s{2,}|$)").Select(m => (Name: m.Value.Trim(), Start: m.Index)).ToList();
        if (!columns.Any(c => c.Name == "Id") || !columns.Any(c => c.Name == "Name")) throw new InvalidDataException("Unsupported WinGet table headers. English WinGet output is required.");
        List<Dictionary<string, string>> result = [];
        foreach (var line in lines.Skip(separator + 1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var row = new Dictionary<string, string>();
            for (int i = 0; i < columns.Count; i++)
            {
                int start = columns[i].Start, end = i + 1 < columns.Count ? columns[i + 1].Start : line.Length;
                row[columns[i].Name] = start >= line.Length ? "" : line[start..Math.Min(line.Length, end)].Trim();
            }
            if (Rules.ValidId(row.GetValueOrDefault("Id")) && !row.GetValueOrDefault("Name", "").Contains('…')) result.Add(row);
        }
        return result;
    }
}
