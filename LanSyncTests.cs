using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace Papergraph;

public static class LanSyncTests
{
    static int checks;
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("Sync test failed: " + message);
        checks++; Console.WriteLine("PASS: " + message);
    }
    static GraphDocument Fixture() => new()
    {
        Title = "中文论文 🌱",
        Nodes = [new() { Id = "a", Title = "细胞会分裂", Note = "用户原始笔记", X = 1 }, new() { Id = "b", Title = "后代形成组织", X = 400 }],
        Edges = [new() { Id = "edge", From = "a", To = "b" }]
    };
    static LanDocument Record(GraphDocument d, string? id = null) => new(id ?? "11111111111111111111111111111111", d.Serialize(), "论文");
    static (LanOffer, LanOffer) Offers(LanDocument original, LanDocument a, LanDocument b)
    {
        var host = new LanOffer { DeviceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", DeviceName = "Mac", Documents = [a], History = new() { [original.Id] = [original] } };
        var guest = new LanOffer { DeviceId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", DeviceName = "Windows", Documents = [b], History = new() { [original.Id] = [original] } };
        return (host, guest);
    }
    static void Merging()
    {
        var original = Record(Fixture());
        var a = Fixture(); a.Nodes[0].Title = "细胞能分裂";
        var pair = Offers(original, Record(a), original);
        var plan = LanMerge.Build(pair.Item1, pair.Item2);
        Check(plan.Conflicts == 0 && plan.Documents.Single().Json == a.Serialize(), "A one-character change propagates without a conflict");
        Check(plan.Changes.Single().Differences.Any(d => d.Host == "细胞能分裂" && d.Guest == "细胞会分裂"), "The preview exposes the exact one-character difference");
        var b = Fixture(); b.Nodes[1].Title = "后代组成组织";
        pair = Offers(original, Record(a), Record(b)); plan = LanMerge.Build(pair.Item1, pair.Item2);
        var merged = GraphDocument.Parse(plan.Documents.Single().Json);
        Check(plan.Conflicts == 0 && merged.Nodes[0].Title == a.Nodes[0].Title && merged.Nodes[1].Title == b.Nodes[1].Title, "Concurrent edits to different propositions merge");
        a = Fixture(); b = Fixture(); a.Nodes[0].AdditionalNotes[0].Body = "Mac evidence"; b.Nodes[0].AdditionalNotes[2].Body = "Windows evidence";
        pair = Offers(original, Record(a), Record(b)); plan = LanMerge.Build(pair.Item1, pair.Item2); merged = GraphDocument.Parse(plan.Documents.Single().Json);
        Check(plan.Conflicts == 0 && merged.Nodes[0].AdditionalNotes[0].Body == "Mac evidence" && merged.Nodes[0].AdditionalNotes[2].Body == "Windows evidence" && merged.Nodes[0].Note == "用户原始笔记", "Different note pages merge while preserving personal notes");
        a = Fixture(); b = Fixture(); a.Nodes[0].Title = "细胞能分裂"; b.Nodes[0].Title = "细胞未分裂";
        pair = Offers(original, Record(a), Record(b)); plan = LanMerge.Build(pair.Item1, pair.Item2);
        Check(plan.Conflicts == 1 && plan.Documents.Count == 2, "Concurrent one-character changes to the same body keep two copies");
        Check(plan.Changes.Single().Differences.Single().Before == "细胞会分裂", "Conflict preview includes the common ancestor");
        Check(LanMerge.Build(pair.Item1, pair.Item2, "guest").Documents.Single().Json == b.Serialize(), "An explicit conflict choice selects the connecting computer");
        var retry = LanMerge.Build(pair.Item1, pair.Item2);
        Check(retry.Documents[1].Id == plan.Documents[1].Id, "Conflict copy identity is stable across retries");
        pair.Item1.History.Clear(); pair.Item2.History.Clear();
        Check(LanMerge.Build(pair.Item1, pair.Item2).Conflicts == 1, "No shared history never guesses which version is newer");
        a = Fixture(); b = Fixture(); a.DeleteNodes(["a"]); b.Nodes[0].Title = "An edited point";
        pair = Offers(original, Record(a), Record(b));
        Check(LanMerge.Build(pair.Item1, pair.Item2).Conflicts == 1, "Deleting a point versus editing it is a conflict");
        a = Fixture(); b = Fixture(); a.DeleteNodes(["a"]); b.Edges.Add(new() { Id = "new-edge", From = "b", To = "a" });
        pair = Offers(original, Record(a), Record(b));
        Check(LanMerge.Build(pair.Item1, pair.Item2).Conflicts == 1, "A merged dangling edge cannot replace a valid graph");
        a = Fixture(); b = Fixture(); a.Nodes[0].BodyColors = [new(0, 2, "#D44747")]; b.Nodes[0].Title = "新的细胞描述";
        pair = Offers(original, Record(a), Record(b));
        Check(LanMerge.Build(pair.Item1, pair.Item2).Conflicts == 1, "Concurrent text and formatting changes stay together");
        a = Fixture(); b = Fixture(); a.AgentReadOnly = true; b.Nodes[1].Title = "A new result";
        pair = Offers(original, Record(a), Record(b)); merged = GraphDocument.Parse(LanMerge.Build(pair.Item1, pair.Item2).Documents.Single().Json);
        Check(merged.AgentReadOnly && merged.Nodes[1].Title == "A new result", "Sync preserves AI protection");
        a = Fixture(); b = Fixture(); a.Nodes[0].Title = "A change"; b.Nodes[0].Title = "A change";
        pair = Offers(original, Record(a), Record(b)); Check(LanMerge.Build(pair.Item1, pair.Item2).Changes.Count == 0, "Identical edits do not conflict");
        pair = Offers(original, original, original with { Category = "结果" });
        Check(LanMerge.Build(pair.Item1, pair.Item2).Documents.Single().Category == "结果", "A one-sided category move propagates");
        pair = Offers(original, original, original); pair.Item2.Documents.Clear();
        Check(LanMerge.Build(pair.Item1, pair.Item2).Documents.Count == 1, "Missing graphs do not cause deletion on the other computer");
        bool bad = false; try { LanMerge.Validate(original with { Id = "../../outside" }); } catch (InvalidDataException) { bad = true; }
        Check(bad, "Remote path-like identities are rejected");
        a = Fixture(); a.Nodes.Reverse(); pair = Offers(original, Record(a), original);
        plan = LanMerge.Build(pair.Item1, pair.Item2);
        Check(plan.Documents.Single().Json == a.Serialize() && plan.Changes.Single().Differences.Any(d => d.Path.EndsWith("/order")), "A reordered collection is preserved and visible in the preview");
    }
    sealed class Library
    {
        public readonly string Root;
        public readonly LanSyncStore Store;
        public List<string> Paths = [];
        public GraphLibraryCatalog Catalog = new();
        public Library(string root, string? title)
        {
            Root = root; Directory.CreateDirectory(root);
            if (title != null) { var d = Fixture(); d.Title = title; var path = Path.Combine(root, "paper.papergraph"); Storage.SaveNew(path, d); Paths.Add(path); }
            Store = new(root);
        }
        public LanOffer Offer(string peer) => Store.Capture(Paths, Catalog, peer);
        public void Apply(LanOffer expected, string peer, IReadOnlyList<LanDocument> docs, Action<int>? fault = null)
        {
            var result = Store.Apply(expected, peer, docs, Paths, Catalog, fault);
            Paths = result.Paths; Catalog = result.Catalog;
        }
    }
    static (LanOffer, LanOffer, LanPlan) Round(Library a, Library b)
    {
        var x = a.Offer(b.Store.DeviceId); var y = b.Offer(a.Store.DeviceId); return (x, y, LanMerge.Build(x, y));
    }
    static void StorageTests(string root)
    {
        var a = new Library(Path.Combine(root, "store-a"), "From Mac");
        var b = new Library(Path.Combine(root, "store-b"), "From Windows");
        var (x, y, plan) = Round(a, b); a.Apply(x, b.Store.DeviceId, plan.Documents); b.Apply(y, a.Store.DeviceId, plan.Documents);
        Check(a.Paths.Count == 2 && b.Paths.Count == 2 && a.Offer(b.Store.DeviceId).Revision == b.Offer(a.Store.DeviceId).Revision, "Initial two-way sync preserves both libraries");
        var (_, _, repeated) = Round(a, b);
        Check(repeated.Changes.Count == 0 && repeated.Documents.Count == 2, "Repeated synchronization does not duplicate graphs");
        var changed = GraphDocument.Parse(File.ReadAllText(a.Paths[0])); changed.Nodes[0].Title = "修改一个字"; Storage.Save(a.Paths[0], changed);
        (x, y, plan) = Round(a, b); a.Apply(x, b.Store.DeviceId, plan.Documents); b.Apply(y, a.Store.DeviceId, plan.Documents);
        Check(b.Paths.Any(p => GraphDocument.Parse(File.ReadAllText(p)).Nodes[0].Title == "修改一个字"), "A later edit updates the existing graph across different file paths");
        Check(Directory.EnumerateFiles(Path.Combine(b.Root, ".lan-sync", "backups"), "*.bak", SearchOption.AllDirectories).Any(), "Pre-sync files are retained as recoverable backups");
        (x, y, plan) = Round(a, b);
        changed.Nodes[0].Note = "New local typing"; Storage.Save(a.Paths[0], changed);
        bool rejected = false; try { a.Apply(x, b.Store.DeviceId, plan.Documents); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && GraphDocument.Parse(File.ReadAllText(a.Paths[0])).Nodes[0].Note == "New local typing", "Edits made after preview prevent stale overwrite");
        (x, y, plan) = Round(a, b);
        var before = b.Paths.ToDictionary(p => p, File.ReadAllText);
        bool failed = false;
        try { b.Apply(y, a.Store.DeviceId, plan.Documents, _ => throw new IOException("Simulated disk failure")); } catch (IOException) { failed = true; }
        Check(failed && before.All(p => File.ReadAllText(p.Key) == p.Value), "A mid-transaction failure restores every original graph");
        Check(!File.Exists(Path.Combine(b.Root, ".lan-sync", "pending.json")), "Rollback clears the pending journal after recovery");
        // Host completed, but the peer lost the response. Retained ancestors allow a safe retry.
        a.Apply(x, b.Store.DeviceId, plan.Documents);
        (x, y, plan) = Round(a, b);
        Check(plan.Conflicts == 0, "Lost acknowledgement uses an older shared ancestor");
        b.Apply(y, a.Store.DeviceId, plan.Documents);
        var reopened = new LanSyncStore(b.Root);
        Check(reopened.DeviceId == b.Store.DeviceId && reopened.Capture(b.Paths, b.Catalog, a.Store.DeviceId).Revision == a.Offer(b.Store.DeviceId).Revision, "Device identity and history survive restart");
        var pathA = a.Paths.Single(p => GraphDocument.Parse(File.ReadAllText(p)).Title == x.Documents[0].Title);
        var pathB = b.Paths.Single(p => GraphDocument.Parse(File.ReadAllText(p)).Title == x.Documents[0].Title);
        var va = GraphDocument.Parse(File.ReadAllText(pathA)); va.Nodes[0].Title = "Host conflict text"; Storage.Save(pathA, va);
        var vb = GraphDocument.Parse(File.ReadAllText(pathB)); vb.Nodes[0].Title = "Guest conflict text"; Storage.Save(pathB, vb);
        (x, y, _) = Round(a, b); plan = LanMerge.Build(x, y, "guest");
        a.Apply(x, b.Store.DeviceId, plan.Documents); b.Apply(y, a.Store.DeviceId, plan.Documents);
        Check(GraphDocument.Parse(File.ReadAllText(pathA)).Nodes[0].Title == "Guest conflict text" &&
            Directory.EnumerateFiles(Path.Combine(a.Root, ".lan-sync", "backups"), "*.papergraph.bak", SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == va.Serialize()),
            "Choosing a conflicting version retains the rejected manuscript in a readable backup");
        var recoveryRoot = Path.Combine(root, "crash-recovery"); Directory.CreateDirectory(Path.Combine(recoveryRoot, ".lan-sync"));
        var target = Path.Combine(recoveryRoot, "graph.papergraph"); var backup = Path.Combine(recoveryRoot, "before.bak"); var staged = Path.Combine(recoveryRoot, "after");
        File.WriteAllText(backup, va.Serialize()); File.WriteAllText(staged, vb.Serialize()); File.WriteAllText(target, vb.Serialize());
        string HashFile(string p) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)));
        var journal = new LanSyncStore.Journal { Entries = [new(target, HashFile(backup), HashFile(staged), true, backup, staged)] };
        var pendingPath = Path.Combine(recoveryRoot, ".lan-sync", "pending.json");
        File.WriteAllText(pendingPath, JsonSerializer.Serialize(journal)); LanSyncStore.Recover(recoveryRoot);
        Check(File.ReadAllText(target) == va.Serialize(), "Startup recovery rolls back an interrupted transaction");
        const string originalExternal = "A newer external edit";
        File.WriteAllText(pendingPath, JsonSerializer.Serialize(journal)); File.WriteAllText(target, originalExternal);
        bool externalKept = false; try { LanSyncStore.Recover(recoveryRoot); } catch (IOException) { externalKept = true; }
        Check(externalKept && File.ReadAllText(target) == originalExternal, "Crash recovery refuses to overwrite a newer external edit");
    }
    static async Task Networking(string root)
    {
        var a = new Library(Path.Combine(root, "wire-a"), "Encrypted host");
        var b = new Library(Path.Combine(root, "wire-b"), "Encrypted guest");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool requested = false; var messages = new List<string>();
        using var host = new LanHost(IPAddress.Loopback,
            peer => Task.FromResult(a.Offer(peer)),
            (_, _, _, _) => { requested = true; return confirmation.Task; },
            (expected, peer, docs) => { a.Apply(expected, peer, docs); return Task.CompletedTask; },
            message => messages.Add(message), true);
        var code = host.Code.ToString();
        Check(code.Length == 76 && LanPairCode.Parse(code, true).Port == host.Code.Port, "Pairing code carries the endpoint, secret and certificate pin");
        bool publicRejected = false; try { LanPairCode.Parse(new LanPairCode(IPAddress.Parse("8.8.8.8"), 443, new byte[16], new byte[32]).ToString()); } catch (InvalidDataException) { publicRejected = true; }
        Check(publicRejected, "Public Internet endpoints cannot be paired");
        var badPin = host.Code with { CertificateHash = new byte[32] };
        bool pinRejected = false;
        try { using var c = await LanConnection.Connect(badPin.ToString(), b.Store.DeviceId, timeout.Token, true); }
        catch (System.Security.Authentication.AuthenticationException) { pinRejected = true; }
        Check(pinRejected, "A certificate not matching the pairing code is rejected");
        var badSecret = host.Code with { Token = new byte[16] };
        bool secretRejected = false;
        try { using var c = await LanConnection.Connect(badSecret.ToString(), b.Store.DeviceId, timeout.Token, true); }
        catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException) { secretRejected = true; }
        Check(secretRejected, "The TLS connection also requires the pairing secret");
        using var client = await LanConnection.Connect(code, b.Store.DeviceId, timeout.Token, true);
        var offer = b.Offer(client.Host.DeviceId);
        var pending = client.Propose(offer, "both", timeout.Token);
        for (int i = 0; i < 100 && !requested; i++) await Task.Delay(10, timeout.Token);
        Check(requested && !pending.IsCompleted && a.Paths.Count == 1 && b.Paths.Count == 1, "Preview and pairing cannot write data before host approval");
        confirmation.SetResult(true);
        var result = await pending;
        b.Apply(offer, a.Store.DeviceId, result); await client.Acknowledge(timeout.Token); await host.Completion.WaitAsync(timeout.Token);
        Check(a.Offer(b.Store.DeviceId).Revision == b.Offer(a.Store.DeviceId).Revision, "Encrypted TCP loopback synchronizes both independent libraries");
        Check(messages.Contains("Sync complete."), "The host reports completion only after the peer acknowledges saving");
        var frame = new MemoryStream(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
        bool oversized = false; try { await LanWire.Read<string>(frame, timeout.Token); } catch (InvalidDataException) { oversized = true; }
        Check(oversized, "Oversized network frames are rejected before allocation");
        bool wrote = false;
        using var rejectingHost = new LanHost(IPAddress.Loopback, peer => Task.FromResult(a.Offer(peer)),
            (_, _, _, _) => Task.FromResult(false), (_, _, _) => { wrote = true; return Task.CompletedTask; }, _ => { }, true);
        using var rejectedClient = await LanConnection.Connect(rejectingHost.Code.ToString(), b.Store.DeviceId, timeout.Token, true);
        bool rejected = false; try { await rejectedClient.Propose(b.Offer(a.Store.DeviceId), "both", timeout.Token); } catch (IOException) { rejected = true; }
        await rejectingHost.Completion.WaitAsync(timeout.Token);
        Check(rejected && !wrote, "Rejecting the host confirmation writes no manuscripts");
    }
    // A deliberately fragmented duplex stream exercises the radio transport seam.
    // This is protocol coverage, not a substitute for physical Mac/Android radio testing.
    sealed class FragmentedStream(Stream inner) : Stream
    {
        public bool Disposed;
        public int Chunks;
        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long x, SeekOrigin o) => throw new NotSupportedException();
        public override void SetLength(long x) => throw new NotSupportedException();
        public override int Read(byte[] b, int o, int n) => inner.Read(b, o, Math.Min(n, 73));
        public override async ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken c = default) => await inner.ReadAsync(b[..Math.Min(b.Length, 73)], c);
        public override void Write(byte[] b, int o, int n) => inner.Write(b, o, n);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken c = default)
        {
            while (b.Length > 0) { int n = Math.Min(97, b.Length); await inner.WriteAsync(b[..n], c); b = b[n..]; Chunks++; }
        }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken c) => inner.FlushAsync(c);
        protected override void Dispose(bool disposing) { Disposed = true; inner.Dispose(); base.Dispose(disposing); }
    }
    static async Task BluetoothTransport(string root)
    {
        var a = new Library(Path.Combine(root, "bt-mac"), "Bluetooth 中文 ✍");
        var b = new Library(Path.Combine(root, "bt-phone"), "Bluetooth 日本語");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int captures = 0; bool requested = false;
        using var host = new LanHost(IPAddress.Loopback, peer => { captures++; return Task.FromResult(a.Offer(peer)); },
            (_, _, _, _) => { requested = true; return confirmation.Task; },
            (expected, peer, docs) => { a.Apply(expected, peer, docs); return Task.CompletedTask; }, _ => { }, true);
        var pair = new BluetoothPairCode("12:34:56:78:9A:BC", host.Code.Token, host.Code.CertificateHash);
        var parsed = BluetoothPairCode.Parse(" "+pair.ToString().Insert(25, "\n")+" ");
        Check(parsed.Address == pair.Address && parsed.Token.SequenceEqual(pair.Token) && parsed.CertificateHash.SequenceEqual(pair.CertificateHash), "Bluetooth code preserves the address, secret and full TLS pin across pasted line breaks");
        foreach (var bad in new[] { host.Code.ToString(), "BT1-123", "BT1-" + new string('A', 72) })
        {
            bool rejected = false; try { BluetoothPairCode.Parse(bad); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed, zero-address or wrong-transport Bluetooth code is rejected");
        }
        FragmentedStream? last = null;
        var previous = BluetoothSync.OpenStream;
        BluetoothSync.OpenStream = async (_, token) =>
        {
            var client = new System.Net.Sockets.TcpClient();
            try { await client.ConnectAsync(IPAddress.Loopback, host.Code.Port, token); return last = new FragmentedStream(client.GetStream()); }
            catch { client.Dispose(); throw; }
        };
        try
        {
            bool pinRejected = false;
            try { using var wrong = await BluetoothSync.Connect((pair with { CertificateHash = new byte[32] }).ToString(), b.Store.DeviceId, timeout.Token); }
            catch (System.Security.Authentication.AuthenticationException) { pinRejected = true; }
            Check(pinRejected && captures == 0 && last!.Disposed, "Bluetooth rejects a substituted certificate and closes the transport before sharing library data");
            bool tokenRejected = false;
            try { using var wrong = await BluetoothSync.Connect((pair with { Token = new byte[16] }).ToString(), b.Store.DeviceId, timeout.Token); }
            catch (IOException) { tokenRejected = true; }
            Check(tokenRejected && captures == 0 && last!.Disposed, "Bluetooth rejects an incorrect secret without capturing documents");
            using (var client = await BluetoothSync.Connect(pair.ToString(), b.Store.DeviceId, timeout.Token))
            {
                var offer = b.Offer(client.Host.DeviceId);
                var pending = client.Propose(offer, "both", timeout.Token);
                for (int i = 0; i < 200 && !requested; i++) await Task.Delay(10, timeout.Token);
                Check(requested && !pending.IsCompleted && a.Paths.Count == 1 && b.Paths.Count == 1, "Bluetooth comparison requires host confirmation before either library changes");
                confirmation.SetResult(true);
                var docs = await pending; b.Apply(offer, client.Host.DeviceId, docs);
                await client.Acknowledge(timeout.Token); await host.Completion.WaitAsync(timeout.Token);
                Check(last!.Chunks > 30 && a.Offer(b.Store.DeviceId).Revision == b.Offer(a.Store.DeviceId).Revision, "Fragmented Bluetooth byte stream completes authenticated bidirectional sync with Unicode data intact");
            }
            Check(last!.Disposed, "Disconnect disposes the owned Bluetooth stream");
        }
        finally { BluetoothSync.OpenStream = previous; }
    }
    public static async Task Run(string root)
    {
        checks = 0; Directory.CreateDirectory(root);
        Merging(); StorageTests(root); await Networking(root); await BluetoothTransport(root);
        Console.WriteLine("PASS: " + checks + " sync checks (LAN and Bluetooth transport)");
        File.WriteAllText(Path.Combine(root, "test-results.txt"), "PASS: " + checks + " sync checks (LAN and simulated Bluetooth transport)\n");
    }
}
