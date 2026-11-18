using System.Text.Json;
using System.Security.Cryptography;

namespace PersistentBridge;

// One atomically replaced snapshot per operation. Never serialize model-provided text or pipeline streams.
internal sealed class History(string root)
{
    private const string Private = "Available in the live session only";
    public static HistoryEntry ForDisk(HistoryEntry entry)
    {
        var op = entry.Op;
        var result = entry.Result;
        return entry with {
            Op = op with { Command = op.ReadOnly ? "Get-Date" : Private, Description = op.ReadOnly ? "Get-Date" : op.Connection,
                Directory = Private, Effects = Private, Risks = "Additional effects unknown", Resources = Private },
            Reason = entry.Reason.Length == 0 ? "" : Private,
            Result = result == null ? null : result with {
                Output = result.Output.Length == 0 ? "" : op.ReadOnly && DateTimeOffset.TryParseExact(result.Output, "O",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _) ? result.Output : Private,
                FileDiff = null, Error = result.Error.Length == 0 ? "" : Private, Directory = Private }
        };
    }

    public void Save(HistoryEntry entry)
    {
        string path = Path.Combine(root, $"operation-{entry.Op.Id:N}.json");
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(ForDisk(entry)));
        File.Move(temp, path, true);
    }

    // Only explicitly reviewed fields may be retained. The credential store and raw diff never enter this path.
    public void KeepReviewed(HistoryEntry entry, HashSet<int> selected)
    {
        var safe = ForDisk(entry);
        var op = safe.Op with {
            Command = selected.Contains(0) ? entry.Op.Command : safe.Op.Command,
            Description = selected.Contains(1) ? entry.Op.Description : safe.Op.Description,
            Effects = selected.Contains(1) ? entry.Op.Effects : safe.Op.Effects,
            Directory = selected.Contains(2) ? entry.Op.Directory : safe.Op.Directory,
            Resources = selected.Contains(2) ? entry.Op.Resources : safe.Op.Resources };
        var result = safe.Result == null ? null : safe.Result with {
            Directory = selected.Contains(2) ? entry.Result!.Directory : safe.Result.Directory,
            Output = selected.Contains(4) ? entry.Result!.Output : safe.Result.Output,
            Error = selected.Contains(5) ? entry.Result!.Error : safe.Result.Error };
        var keep = safe with { Op = op, Result = result, Reason = selected.Contains(3) ? entry.Reason : safe.Reason };
        var file = Path.Combine(root, $"operation-{op.Id:N}.json.reviewed");
        if (selected.Count == 0) { File.Delete(file); return; }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(keep);
        try {
            File.WriteAllBytes(file+".tmp",ProtectedData.Protect(bytes,op.Id.ToByteArray(),DataProtectionScope.CurrentUser));
            File.Move(file+".tmp",file,true);
        } finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    static HistoryEntry ReadReviewed(string file, HistoryEntry fallback)
    {
        if (!File.Exists(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) return fallback;
        try {
            byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(file),fallback.Op.Id.ToByteArray(),DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<HistoryEntry>(bytes) ?? fallback; }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        } catch (Exception ex) when (ex is JsonException or CryptographicException or IOException) {
            Console.Error.WriteLine("Reviewed fields unavailable for " + fallback.Op.Id);
            return fallback;
        }
    }

    public HistoryEntry RestoreReviewed(HistoryEntry entry) =>
        ReadReviewed(Path.Combine(root, $"operation-{entry.Op.Id:N}.json.reviewed"), ForDisk(entry));

    public IEnumerable<HistoryEntry> Load()
    {
        foreach (string file in Directory.EnumerateFiles(root, "operation-*.json*"))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30)) { File.Delete(file); continue; }
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            HistoryEntry? row;
            try { row = JsonSerializer.Deserialize<HistoryEntry>(File.ReadAllText(file)); }
            catch (JsonException) { Console.Error.WriteLine("Unreadable history entry: " + Path.GetFileName(file)); continue; }
            if (row?.Op == null) continue;
            row = ReadReviewed(file+".reviewed",row);
            yield return row.Ended == null ? row with { Status = row.Started.HasValue ? "result_unknown" : "interrupted_before_execution", Ended = File.GetLastWriteTimeUtc(file) } : row;
        }
    }
}
