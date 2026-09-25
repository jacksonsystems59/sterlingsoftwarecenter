using System.IO.Compression;
using System.Security.Cryptography;
namespace Sterling.Core;
public static class IntegrityBundle
{
    public static void Pack(string directory, string zip)
    {
        var hashes = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != "integrity.json").ToDictionary(f => Path.GetRelativePath(directory, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
        Storage.Save(Path.Combine(directory, "integrity.json"), hashes);
        string temp = zip + "." + Guid.NewGuid().ToString("N") + ".tmp";
        ZipFile.CreateFromDirectory(directory, temp); File.Move(temp, zip, true);
    }
    public static async Task<string> Open(string zip)
    {
        string root = Path.Combine(Storage.Home, "opened-bundles", Guid.NewGuid().ToString("N"));
        AppUpdates.ExtractSafe(zip, root);
        await VerifyDirectory(root); return root;
    }
    public static async Task VerifyDirectory(string root)
    {
        var hashes = Storage.Load<Dictionary<string, string>>(Path.Combine(root, "integrity.json"));
        if (hashes.Count == 0 || hashes.Count > 4999) throw new InvalidDataException("Bundle integrity list is invalid.");
        foreach (var (name, hash) in hashes) await AppUpdates.Verify(AppUpdates.SafePath(root, name), hash);
        var actual = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f)).Where(f => f != "integrity.json");
        if (actual.Any(f => !hashes.ContainsKey(f))) throw new InvalidDataException("Unexpected file in bundle.");
    }
}
