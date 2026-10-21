using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Papergraph;

// Network documents never contain filesystem paths. Identity belongs to a library's
// sidecar index, so enabling sync does not rewrite existing manuscripts.
public sealed record LanDocument(string Id, string Json, string Category)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Hash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json + "\0" + Category)));
    [System.Text.Json.Serialization.JsonIgnore]
    public string Title => GraphDocument.Parse(Json).Title;
}
public sealed class LanOffer
{
    public int Version { get; set; } = 1;
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public List<LanDocument> Documents { get; set; } = [];
    public Dictionary<string, List<LanDocument>> History { get; set; } = [];
    public void Validate()
    {
        if (Version != 1 || !LanMerge.ValidId(DeviceId) || string.IsNullOrWhiteSpace(DeviceName) || DeviceName.Length > 80 ||
            Documents == null || Documents.Count > 500 || History == null || History.Count > 1000)
            throw new InvalidDataException("Invalid sync library.");
        var ids = new HashSet<string>();
        long size = 0;
        foreach (var d in Documents)
        {
            LanMerge.Validate(d);
            if (!ids.Add(d.Id)) throw new InvalidDataException("Duplicate sync identity.");
            size += Encoding.UTF8.GetByteCount(d.Json);
        }
        foreach (var (id, versions) in History)
        {
            if (!LanMerge.ValidId(id) || versions == null || versions.Count > 4) throw new InvalidDataException("Invalid sync history.");
            foreach (var d in versions)
            {
                LanMerge.Validate(d);
                if (d.Id != id) throw new InvalidDataException("Invalid sync history identity.");
                size += Encoding.UTF8.GetByteCount(d.Json);
            }
        }
        if (size > 48_000_000) throw new InvalidDataException("This library is too large for one sync (48 MB including history).");
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Revision => string.Join(";", Documents.OrderBy(d => d.Id).Select(d => d.Id + ":" + d.Hash));
}
public sealed record LanDifference(string Path, string Before, string Host, string Guest);
public sealed record LanChange(string Title, string Kind, List<LanDifference> Differences);
public sealed class LanPlan
{
    public List<LanDocument> Documents { get; } = [];
    public List<LanChange> Changes { get; } = [];
    public int Conflicts => Changes.Count(c => c.Kind == "Conflict");
}
public static class LanMerge
{
    public static bool ValidId(string? id) => id is { Length: 32 } && id.All(Uri.IsHexDigit);
    public static void Validate(LanDocument? d)
    {
        if (d == null || !ValidId(d.Id) || d.Json == null || Encoding.UTF8.GetByteCount(d.Json) > 20_000_000 ||
            d.Category == null || d.Category.Length > 200) throw new InvalidDataException("Invalid sync document.");
        if (GraphDocument.Parse(d.Json).Serialize() != d.Json) throw new InvalidDataException("Unsupported or non-canonical sync document.");
    }
    public static LanPlan Build(LanOffer host, LanOffer guest, string choice = "both")
    {
        host.Validate(); guest.Validate();
        if (host.DeviceId == guest.DeviceId) throw new InvalidDataException("These libraries have the same device identity. Use a separately created library on the other computer.");
        if (choice is not ("both" or "host" or "guest")) throw new InvalidDataException("Invalid conflict choice.");
        var plan = new LanPlan();
        var a = host.Documents.ToDictionary(d => d.Id); var b = guest.Documents.ToDictionary(d => d.Id);
        foreach (var id in a.Keys.Union(b.Keys).OrderBy(x => x))
        {
            if (!a.TryGetValue(id, out var left)) { plan.Documents.Add(b[id]); plan.Changes.Add(new(b[id].Title, "Add to host", [])); continue; }
            if (!b.TryGetValue(id, out var right)) { plan.Documents.Add(left); plan.Changes.Add(new(left.Title, "Add to guest", [])); continue; }
            if (left.Hash == right.Hash) { plan.Documents.Add(left); continue; }
            var basesA = host.History.GetValueOrDefault(id) ?? [];
            var basesB = guest.History.GetValueOrDefault(id) ?? [];
            // Previous agreed states handle lost acknowledgements without losing edits.
            var ancestor = basesA.FirstOrDefault(x => basesB.Any(y => y.Hash == x.Hash));
            var differences = new List<LanDifference>();
            LanDocument? merged = null;
            if (ancestor != null)
            {
                if (left.Hash == ancestor.Hash) merged = right;
                else if (right.Hash == ancestor.Hash) merged = left;
                else
                {
                    var json = Merge(JsonNode.Parse(ancestor.Json), JsonNode.Parse(left.Json), JsonNode.Parse(right.Json), "", differences);
                    var category = Merge(JsonValue.Create(ancestor.Category), JsonValue.Create(left.Category), JsonValue.Create(right.Category), "Category", differences)?.GetValue<string>() ?? "";
                    if (differences.Count == 0)
                    {
                        try { merged = new(id, GraphDocument.Parse(json!.ToJsonString()).Serialize(), category); }
                        catch (Exception ex) when (ex is InvalidDataException or JsonException)
                        { differences.Add(new("Graph structure", "", left.Title, right.Title)); }
                    }
                }
            }
            else differences.Add(new("No common sync history", "", left.Title, right.Title));
            if (merged != null)
            {
                merged = PreserveProtection(merged, left, right);
                plan.Documents.Add(merged);
                var preview = new List<LanDifference>();
                Compare(JsonNode.Parse(ancestor?.Json ?? left.Json), JsonNode.Parse(left.Json), JsonNode.Parse(right.Json), "", preview);
                if (left.Category != right.Category) preview.Add(new("Category", ancestor?.Category ?? "", left.Category, right.Category));
                plan.Changes.Add(new(left.Title, merged.Hash == left.Hash ? "Update guest" : merged.Hash == right.Hash ? "Update host" : "Merge", preview));
            }
            else
            {
                plan.Changes.Add(new(left.Title, "Conflict", differences));
                var primary = choice == "guest" ? right : left;
                plan.Documents.Add(PreserveProtection(primary, left, right));
                if (choice == "both")
                {
                    // Stable identity prevents repeated copies after an interrupted sync.
                    var copyId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id + ":" + right.Hash)))[..32].ToLowerInvariant();
                    var copy = GraphDocument.Parse(right.Json); copy.Title += " (sync conflict · " + guest.DeviceName + ")";
                    if (!a.ContainsKey(copyId) && !b.ContainsKey(copyId)) plan.Documents.Add(new(copyId, copy.Serialize(), right.Category));
                }
            }
        }
        if (plan.Documents.Count > 500) throw new InvalidDataException("The combined library exceeds the 500-graph sync limit.");
        // Leave room for at least one complete shared ancestor in the next offer.
        if (plan.Documents.Sum(d => (long)Encoding.UTF8.GetByteCount(d.Json)) > 24_000_000)
            throw new InvalidDataException("The combined graphs exceed the 24 MB sync limit.");
        return plan;
    }
    static LanDocument PreserveProtection(LanDocument result, LanDocument a, LanDocument b)
    {
        var d = GraphDocument.Parse(result.Json);
        d.AgentReadOnly |= GraphDocument.Parse(a.Json).AgentReadOnly || GraphDocument.Parse(b.Json).AgentReadOnly;
        return result with { Json = d.Serialize() };
    }
    static bool Equal(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);
    static string Describe(JsonNode? n)
    {
        if (n == null) return "∅";
        if (n is JsonValue v && v.TryGetValue<string>(out var text)) return text;
        return n.ToJsonString();
    }
    static JsonNode? Merge(JsonNode? original, JsonNode? a, JsonNode? b, string path, List<LanDifference> conflicts)
    {
        if (Equal(a, b)) return a?.DeepClone();
        if (Equal(original, a)) return b?.DeepClone();
        if (Equal(original, b)) return a?.DeepClone();
        if (original is JsonObject o && a is JsonObject l && b is JsonObject r)
        {
            // Text and its UTF-16 formatting offsets must move together.
            if (o.ContainsKey("BodyColors") || l.ContainsKey("BodyColors") || r.ContainsKey("BodyColors"))
            {
                var aChanged = !Equal(o["Title"], l["Title"]) || !Equal(o["BodyColors"], l["BodyColors"]);
                var bChanged = !Equal(o["Title"], r["Title"]) || !Equal(o["BodyColors"], r["BodyColors"]);
                if (aChanged && bChanged && (!Equal(l["Title"], r["Title"]) || !Equal(l["BodyColors"], r["BodyColors"])))
                { conflicts.Add(new(path + "/Title", Describe(o["Title"]), Describe(l["Title"]), Describe(r["Title"]))); return a.DeepClone(); }
            }
            var result = new JsonObject();
            foreach (var key in o.Select(p => p.Key).Union(l.Select(p => p.Key)).Union(r.Select(p => p.Key)))
            {
                var value = Merge(o[key], l[key], r[key], path + "/" + key, conflicts);
                if (value != null || l.ContainsKey(key) && r.ContainsKey(key)) result[key] = value;
            }
            return result;
        }
        if (original is JsonArray oa && a is JsonArray la && b is JsonArray ra)
        {
            if (path is "/Nodes" or "/Edges" or "/Regions")
            {
                var om = Map(oa); var lm = Map(la); var rm = Map(ra);
                var common = om.Keys.Where(x => lm.ContainsKey(x) && rm.ContainsKey(x)).ToHashSet();
                var os = om.Keys.Where(common.Contains).ToArray();
                var ls = lm.Keys.Where(common.Contains).ToArray(); var rs = rm.Keys.Where(common.Contains).ToArray();
                if (!ls.SequenceEqual(os) && !rs.SequenceEqual(os) && !ls.SequenceEqual(rs))
                { conflicts.Add(new(path + "/order", string.Join(",", os), string.Join(",", ls), string.Join(",", rs))); return a.DeepClone(); }
                var order = (ls.SequenceEqual(os) ? rm.Keys : lm.Keys).ToList();
                foreach (var key in lm.Keys.Concat(rm.Keys).Concat(om.Keys)) if (!order.Contains(key)) order.Add(key);
                var array = new JsonArray();
                foreach (var key in order)
                {
                    var value = Merge(om.GetValueOrDefault(key), lm.GetValueOrDefault(key), rm.GetValueOrDefault(key), path + "/" + key, conflicts);
                    if (value != null) array.Add(value);
                }
                return array;
            }
            if (path.EndsWith("/AdditionalNotes", StringComparison.Ordinal) && oa.Count == la.Count && oa.Count == ra.Count)
            {
                var array = new JsonArray();
                for (int i = 0; i < oa.Count; i++) array.Add(Merge(oa[i], la[i], ra[i], path + "/" + (i + 2), conflicts));
                return array;
            }
        }
        conflicts.Add(new(path, Describe(original), Describe(a), Describe(b)));
        return a?.DeepClone();
    }
    static Dictionary<string, JsonNode?> Map(JsonArray array) => array.ToDictionary(n => n!["Id"]!.GetValue<string>());
    static void Compare(JsonNode? original, JsonNode? a, JsonNode? b, string path, List<LanDifference> changes)
    {
        if (Equal(a, b) || changes.Count >= 200) return;
        if (a is JsonObject l && b is JsonObject r)
        {
            foreach (var key in l.Select(p => p.Key).Union(r.Select(p => p.Key)))
                Compare((original as JsonObject)?[key], l[key], r[key], path + "/" + key, changes);
        }
        else if (a is JsonArray la && b is JsonArray ra && path is "/Nodes" or "/Edges" or "/Regions")
        {
            var lm = Map(la); var rm = Map(ra); var om = original is JsonArray oa ? Map(oa) : [];
            if (!lm.Keys.SequenceEqual(rm.Keys)) changes.Add(new(path + "/order", string.Join(",", om.Keys), string.Join(",", lm.Keys), string.Join(",", rm.Keys)));
            foreach (var key in lm.Keys.Union(rm.Keys))
                Compare(om.GetValueOrDefault(key), lm.GetValueOrDefault(key), rm.GetValueOrDefault(key), path + "/" + key, changes);
        }
        else if (a is JsonArray ln && b is JsonArray rn && path.EndsWith("/AdditionalNotes") && ln.Count == rn.Count)
        {
            for (int i = 0; i < ln.Count; i++) Compare((original as JsonArray)?[i], ln[i], rn[i], path + "/" + (i + 2), changes);
        }
        else changes.Add(new(path, Describe(original), Describe(a), Describe(b)));
    }
}
