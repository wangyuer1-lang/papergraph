using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Papergraph;

public sealed class LanState
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public Dictionary<string, string> Paths { get; set; } = new(PathIdentity.Comparer);
    public Dictionary<string, Dictionary<string, List<LanDocument>>> Peers { get; set; } = [];
}
public sealed record LanApplied(List<string> Paths, GraphLibraryCatalog Catalog, string BackupDirectory);
public sealed class LanSyncStore
{
    readonly string directory, statePath;
    LanState state;
    public string DeviceId => state.DeviceId;
    public LanSyncStore(string dataDirectory)
    {
        directory = Path.GetFullPath(dataDirectory);
        statePath = Path.Combine(directory, ".lan-sync", "state.json");
        Recover(directory);
        state = File.Exists(statePath) ? JsonSerializer.Deserialize<LanState>(File.ReadAllText(statePath))! : new();
        if (state == null || !LanMerge.ValidId(state.DeviceId) || state.Paths == null || state.Peers == null ||
            state.Paths.Any(p => !Path.IsPathFullyQualified(p.Key) || !LanMerge.ValidId(p.Value)))
            throw new InvalidDataException("Could not read sync history. Original files have been kept.");
        state.Paths = new(state.Paths, PathIdentity.Comparer);
    }
    public LanOffer Capture(IEnumerable<string> paths, GraphLibraryCatalog catalog, string peerId)
    {
        var offer = new LanOffer { DeviceId = state.DeviceId, DeviceName = Environment.MachineName[..Math.Min(80, Environment.MachineName.Length)] };
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(PathIdentity.Comparer))
        {
            if (!File.Exists(path)) throw new IOException("A graph is missing. Reopen or remove it from the library before syncing.");
            if (new FileInfo(path).Length > 20_000_000) throw new InvalidDataException("A graph is too large to sync (20 MB).");
            if (!state.Paths.TryGetValue(path, out var id)) state.Paths[path] = id = Guid.NewGuid().ToString("N");
            var graph = GraphDocument.Parse(File.ReadAllText(path));
            var category = catalog.Categories.FirstOrDefault(c => c.Id == catalog.Assignments.GetValueOrDefault(path))?.Name ?? "";
            offer.Documents.Add(new(id, graph.Serialize(), category));
        }
        if (state.Peers.TryGetValue(peerId, out var history))
        {
            var ids = offer.Documents.Select(d => d.Id).ToHashSet();
            offer.History = history.Where(p => ids.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value.ToList());
            long size = offer.Documents.Sum(d => (long)Encoding.UTF8.GetByteCount(d.Json)) + offer.History.Values.SelectMany(v => v).Sum(d => (long)Encoding.UTF8.GetByteCount(d.Json));
            while (size > 48_000_000 && offer.History.Values.Any(v => v.Count > 0))
            {
                var versions = offer.History.Values.OrderByDescending(v => v.Count).First();
                size -= Encoding.UTF8.GetByteCount(versions[^1].Json); versions.RemoveAt(versions.Count - 1);
            }
        }
        offer.Validate();
        WriteAtomic(statePath, JsonSerializer.Serialize(state));
        return offer;
    }
    public LanApplied Apply(LanOffer expected, string peerId, IReadOnlyList<LanDocument> documents,
        IEnumerable<string> currentPaths, GraphLibraryCatalog catalog, Action<int>? afterWrite = null)
    {
        if (!LanMerge.ValidId(peerId) || peerId == DeviceId) throw new InvalidDataException("Invalid sync peer.");
        var paths = currentPaths.Select(Path.GetFullPath).Distinct(PathIdentity.Comparer).ToList();
        var actual = Capture(paths, catalog, peerId);
        if (actual.Revision != expected.Revision) throw new InvalidOperationException("Your library changed after the preview. Reconnect to compare it again.");
        if (documents.Count > 500 || documents.Select(d => d.Id).Distinct().Count() != documents.Count)
            throw new InvalidDataException("Invalid sync result.");
        foreach (var d in documents) LanMerge.Validate(d);
        if (expected.Documents.Any(d => !documents.Any(x => x.Id == d.Id))) throw new InvalidDataException("Sync cannot delete local graphs.");
        var next = JsonSerializer.Deserialize<LanState>(JsonSerializer.Serialize(state))!;
        next.Paths = new(next.Paths, PathIdentity.Comparer);
        var nextCatalog = catalog.Copy();
        var localIds = paths.ToDictionary(p => state.Paths[p], p => p);
        var writes = new Dictionary<string, string>(PathIdentity.Comparer);
        foreach (var item in documents)
        {
            if (!localIds.TryGetValue(item.Id, out var path))
            {
                // Remote names are never interpreted as paths.
                path = Path.Combine(directory, "synced-" + item.Id + ".papergraph");
                if (File.Exists(path)) throw new IOException("A sync destination already exists. No files were replaced.");
                paths.Add(path); next.Paths[path] = item.Id;
            }
            var before = actual.Documents.FirstOrDefault(d => d.Id == item.Id);
            if (before == null || before.Json != item.Json) writes[path] = item.Json;
            if (item.Category.Length == 0) nextCatalog.Assignments.Remove(path);
            else
            {
                var category = nextCatalog.Categories.FirstOrDefault(c => c.Name == item.Category);
                if (category == null)
                {
                    category = new() { Id = Guid.NewGuid().ToString("N"), Name = item.Category };
                    nextCatalog.Categories.Add(category);
                }
                nextCatalog.Assignments[path] = category.Id;
            }
        }
        var prior = next.Peers.GetValueOrDefault(peerId) ?? [];
        next.Peers[peerId] = documents.ToDictionary(d => d.Id,
            d => new[] { d }.Concat(prior.GetValueOrDefault(d.Id) ?? []).DistinctBy(x => x.Hash).Take(4).ToList());
        while (next.Peers.Count > 16) next.Peers.Remove(next.Peers.Keys.First(k => k != peerId));
        writes[Path.Combine(directory, "graphs.json")] = JsonSerializer.Serialize(paths);
        writes[Path.Combine(directory, "graph-categories.json")] = JsonSerializer.Serialize(nextCatalog, GraphDocument.Options);
        GraphLibraryCatalog.Parse(writes[Path.Combine(directory, "graph-categories.json")]);
        writes[statePath] = JsonSerializer.Serialize(next);
        var backup = Commit(directory, writes, afterWrite);
        state = next;
        return new(paths, nextCatalog, backup);
    }
    internal sealed record JournalEntry(string Target, string BeforeHash, string AfterHash, bool Existed, string Backup, string Staged);
    internal sealed class Journal
    {
        public bool Committed { get; set; }
        public List<JournalEntry> Entries { get; set; } = [];
    }
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static string Commit(string directory, Dictionary<string, string> writes, Action<int>? afterWrite)
    {
        var backup = Path.Combine(directory, ".lan-sync", "backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backup);
        var pending = Path.Combine(directory, ".lan-sync", "pending.json");
        var journal = new Journal();
        foreach (var (target, content) in writes)
        {
            var number = journal.Entries.Count.ToString();
            var saved = Path.Combine(backup, number + "-" + Path.GetFileName(target) + ".bak");
            var staged = Path.Combine(backup, number + ".after");
            WriteAtomic(staged, content);
            var existed = File.Exists(target);
            if (existed) File.Copy(target, saved);
            journal.Entries.Add(new(target, existed ? Hash(saved) : "", Hash(staged), existed, saved, staged));
        }
        WriteAtomic(pending, JsonSerializer.Serialize(journal));
        try
        {
            for (int i = 0; i < journal.Entries.Count; i++)
            {
                var entry = journal.Entries[i];
                if (entry.Existed != File.Exists(entry.Target) || entry.Existed && Hash(entry.Target) != entry.BeforeHash)
                    throw new IOException("A file changed during sync. No external edits will be overwritten.");
                WriteAtomic(entry.Target, File.ReadAllText(entry.Staged));
                afterWrite?.Invoke(i);
            }
            journal.Committed = true;
            WriteAtomic(pending, JsonSerializer.Serialize(journal));
            File.Move(pending, Path.Combine(backup, "receipt.json"));
            return backup;
        }
        catch
        {
            Recover(directory);
            throw;
        }
    }
    // Recovery runs before the application opens its library. Rollback only touches
    // files still matching this transaction, never a newer external edit.
    public static void Recover(string directory)
    {
        var pending = Path.Combine(directory, ".lan-sync", "pending.json");
        if (!File.Exists(pending)) return;
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(pending)) ?? throw new IOException("Invalid pending sync.");
        if (!journal.Committed)
        {
            foreach (var e in journal.Entries.AsEnumerable().Reverse())
            {
                if (!File.Exists(e.Target)) { if (e.Existed) WriteAtomic(e.Target, File.ReadAllText(e.Backup)); continue; }
                var current = Hash(e.Target);
                if (e.Existed && current == e.BeforeHash) continue;
                if (current != e.AfterHash) throw new IOException("Sync recovery found a newer external edit. The original and incoming files are kept in .lan-sync/backups.");
                if (e.Existed) WriteAtomic(e.Target, File.ReadAllText(e.Backup));
                else File.Move(e.Target, e.Staged + ".rolled-back-" + Guid.NewGuid().ToString("N"));
            }
        }
        File.Move(pending, pending + "." + Guid.NewGuid().ToString("N") + ".recovered");
    }
    internal static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(Encoding.UTF8.GetBytes(content));
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
