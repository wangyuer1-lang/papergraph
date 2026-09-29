using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Papergraph;

public class Proposition
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Title {get;set;}="";
    public string Caption {get;set;}="";
    public string Note {get;set;}="";
    public string Color {get;set;}="#6D9ED5";
    public string Kind {get;set;}="point";
    public bool Expanded {get;set;}
    public string? Parent {get;set;}
    public Dictionary<string,List<string>> RegionMembership {get;set;}=[];
    public double X {get;set;}
    public double Y {get;set;}
}
public class Relation
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string From {get;set;}="";
    public string To {get;set;}="";
    public string Label {get;set;}="Support";
    public string Caption {get;set;}="";
    public string Note {get;set;}="";
    public string Direction {get;set;}="forward";
}
public class Region
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Title {get;set;}="Untitled □";
    public string Note {get;set;}="";
    public string Color {get;set;}="";
    public bool IsAbsolute {get;set;}
    public double X {get;set;}
    public double Y {get;set;}
    public double Width {get;set;}=260;
    public double Height {get;set;}=180;
    public string? Parent {get;set;}
    public List<string> Members {get;set;}=[];
}
public class GraphDocument
{
    public int Version {get;set;}=1;
    public string Title {get;set;}="Untitled paper";
    public List<Proposition> Nodes {get;set;}=[];
    public List<Relation> Edges {get;set;}=[];
    public List<Region> Regions {get;set;}=[];
    public static readonly JsonSerializerOptions Options=new(){WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    public string Serialize()=>JsonSerializer.Serialize(this,Options);
    public static GraphDocument Parse(string json)
    {
        if(json.Length>20_000_000) throw new InvalidDataException("This file is too large. The limit is 20 MB.");
        var d=JsonSerializer.Deserialize<GraphDocument>(json,Options)??throw new InvalidDataException("The file is empty."); d.Validate(); return d;
    }
    public void Validate()
    {
        if(Version!=1||Nodes==null||Edges==null||Regions==null||Title==null) throw new InvalidDataException("Unsupported papergraph file format.");
        if(Nodes.Count>5000||Edges.Count>20000) throw new InvalidDataException("A document supports up to 5,000 propositions and 20,000 relations.");
        if(Regions.Any(r=>r!=null&&(r.Color==null||r.Color.Length>0&&!Regex.IsMatch(r.Color,"^#[0-9a-fA-F]{6}$"))))throw new InvalidDataException("Invalid □ color.");
        var ids=new HashSet<string>();
        foreach(var n in Nodes) if(n==null||string.IsNullOrEmpty(n.Id)||!ids.Add(n.Id)||n.Title==null||n.Caption==null||n.Note==null||n.Color==null||!Regex.IsMatch(n.Color,"^#[0-9a-fA-F]{6}$")||!(n.Kind=="point"||n.Kind=="circle")||!double.IsFinite(n.X)||!double.IsFinite(n.Y)||Math.Abs(n.X)>1e7||Math.Abs(n.Y)>1e7) throw new InvalidDataException("Invalid proposition data.");
        foreach(var n in Nodes) { var seen=new HashSet<string>{n.Id}; var p=n.Parent; while(p!=null) { var a=Node(p); if(a==null||a.Kind!="circle"||!seen.Add(p)) throw new InvalidDataException("Invalid ◎ hierarchy."); p=a.Parent; } }
        foreach(var e in Edges) if(e==null||string.IsNullOrEmpty(e.Id)||!ids.Add(e.Id)||Node(e.From)==null||Node(e.To)==null||e.From==e.To||string.IsNullOrWhiteSpace(e.Label)||e.Caption==null||e.Note==null||!(e.Direction is "forward" or "reverse" or "both")) throw new InvalidDataException("Invalid relation data.");
        foreach(var r in Regions) if(r==null||string.IsNullOrEmpty(r.Id)||!ids.Add(r.Id)||r.Title==null||r.Note==null||r.Members==null||r.Parent!=null&&Node(r.Parent)?.Kind!="circle"||!double.IsFinite(r.X)||!double.IsFinite(r.Y)||!double.IsFinite(r.Width)||!double.IsFinite(r.Height)||Math.Abs(r.X)>1e7||Math.Abs(r.Y)>1e7||r.Width<40||r.Height<40||r.Width>1e7||r.Height>1e7||!r.IsAbsolute&&(r.Members.Count==0||r.Members.Distinct().Count()!=r.Members.Count||r.Members.Any(id=>Node(id)==null||Node(id)!.Parent!=r.Parent))) throw new InvalidDataException("Invalid □ data.");
    }
    public bool MaterializeRegions()
    {
        bool changed=false;foreach(var r in Regions.Where(r=>!r.IsAbsolute)){var box=System.Windows.Rect.Empty;foreach(var id in r.Members)if(Node(id) is Proposition n)box.Union(GraphStyle.Bounds(n));if(box.IsEmpty)continue;box.Inflate(28,28);r.X=box.X;r.Y=box.Y;r.Width=box.Width;r.Height=box.Height;r.IsAbsolute=true;r.Members.Clear();changed=true;}return changed;
    }
    public Proposition? Node(string? id)=>Nodes.FirstOrDefault(n=>n.Id==id);
    public List<Proposition> Visible(string? scope)=>Nodes.Where(n=>n.Parent==scope).ToList();
    public string? Project(string id,string? scope)
    {
        var n=Node(id); var seen=new HashSet<string>();
        while(n!=null&&seen.Add(n.Id)) { if(n.Parent==scope) return n.Id; if(n.Parent==null)return null; n=Node(n.Parent); } return null;
    }
    public List<(Relation Edge,string From,string To)> VisibleEdges(string? scope)
    {
        var result=new List<(Relation,string,string)>();
        foreach(var e in Edges) { var a=Project(e.From,scope); var b=Project(e.To,scope); if(a!=null&&b!=null&&a!=b)result.Add((e,a,b)); }return result;
    }
    public bool Connected(IEnumerable<string> members,string? scope)
    {
        var set=members.ToHashSet(); if(set.Count==0)return false;
        var reached=new HashSet<string>{set.First()}; bool changed=true;
        var edges=VisibleEdges(scope);
        while(changed) { changed=false; foreach(var e in edges)if(set.Contains(e.From)&&set.Contains(e.To)) { if(reached.Contains(e.From)&&reached.Add(e.To))changed=true; if(reached.Contains(e.To)&&reached.Add(e.From))changed=true; } }return reached.Count==set.Count;
    }
    public Proposition Collapse(IEnumerable<string> members,string? scope)
    {
        var ids=members.ToHashSet();
        if(ids.Count<2||ids.Any(id=>Node(id)?.Parent!=scope)||!Connected(ids,scope))throw new InvalidOperationException("A ◎ needs at least two connected propositions. Add a connection or create a □.");
        var selected=Nodes.Where(n=>ids.Contains(n.Id)).ToList();
        var centerX=selected.Average(n=>GraphStyle.Center(n).X);var centerY=selected.Average(n=>GraphStyle.Center(n).Y);
        var circle=new Proposition {Kind="circle",Expanded=true,Title="",Color="#A291C9",Parent=scope,X=centerX-78,Y=centerY-78};
        Nodes.Add(circle); foreach(var n in selected)n.Parent=circle.Id;
        foreach(var r in Regions.Where(r=>r.Parent==scope&&!r.IsAbsolute).ToList())
        {
            var included=r.Members.Where(ids.Contains).ToList();
            if(included.Count==0)continue;
            if(included.Count==r.Members.Count)r.Parent=circle.Id;
            else {circle.RegionMembership[r.Id]=included;r.Members.RemoveAll(ids.Contains);r.Members.Add(circle.Id);}
        }
        return circle;
    }
    public void Dissolve(string id)
    {
        var c=Node(id)??throw new InvalidOperationException("The ◎ does not exist.");
        bool keep=c.Title.Length>0||c.Caption.Length>0||c.Note.Length>0||Edges.Any(e=>e.From==id||e.To==id);
        var groupBox=GraphGroups.Bounds(this,c);var groupCenter=new System.Windows.Point(groupBox.X+groupBox.Width/2,groupBox.Y+groupBox.Height/2);
        var children=Visible(id);var shift=c.Expanded||children.Count==0?new System.Windows.Vector():GraphStyle.Center(c)-new System.Windows.Point(children.Average(n=>GraphStyle.Center(n).X),children.Average(n=>GraphStyle.Center(n).Y));
        if(shift.Length<.001)shift=new System.Windows.Vector();
        foreach(var n in children){n.Parent=c.Parent;n.X+=shift.X;n.Y+=shift.Y;}
        foreach(var r in Regions.Where(r=>r.Parent==id)){r.Parent=c.Parent;if(r.IsAbsolute){r.X+=shift.X;r.Y+=shift.Y;}}
        foreach(var r in Regions.Where(r=>r.Members.Contains(id)))
        {
            r.Members.Remove(id);
            var restored=c.RegionMembership!=null&&c.RegionMembership.TryGetValue(r.Id,out var prior)?children.Where(n=>prior.Contains(n.Id)):children;
            r.Members.AddRange(restored.Select(n=>n.Id).Where(child=>!r.Members.Contains(child)));
        }
        if(keep){var center=c.Expanded?groupCenter:GraphStyle.Center(c);c.Kind="point";c.Expanded=false;c.X=center.X-125;c.Y=center.Y-60;c.RegionMembership=[];}else Nodes.Remove(c);Regions.RemoveAll(r=>!r.IsAbsolute&&r.Members.Count==0);
    }
    public HashSet<string> Descendants(IEnumerable<string> roots)
    {
        var ids=roots.ToHashSet(); bool changed=true; while(changed){changed=false;foreach(var n in Nodes)if(n.Parent!=null&&ids.Contains(n.Parent)&&ids.Add(n.Id))changed=true;}return ids;
    }
    public void DeleteNodes(IEnumerable<string> roots)
    {
        var ids=Descendants(roots); Nodes.RemoveAll(n=>ids.Contains(n.Id));Edges.RemoveAll(e=>ids.Contains(e.From)||ids.Contains(e.To));Regions.RemoveAll(r=>r.Parent!=null&&ids.Contains(r.Parent));foreach(var r in Regions)r.Members.RemoveAll(ids.Contains);Regions.RemoveAll(r=>!r.IsAbsolute&&r.Members.Count==0);
    }
    public string Markdown()
    {
        var b=new StringBuilder("# "+Title+"\n\n");
        string Name(Proposition? n)=>n==null?"":string.IsNullOrWhiteSpace(n.Caption)?n.Title:n.Caption;
        void Walk(string? parent,int level)
        {
            foreach(var n in Visible(parent))
            {
                var heading=Name(n);b.AppendLine(new string('#',Math.Min(level,6))+" "+(heading.Length==0?"Untitled proposition":heading));b.AppendLine();
                if(!string.IsNullOrWhiteSpace(n.Caption)&&n.Title.Length>0){b.AppendLine(n.Title);b.AppendLine();}
                if(n.Note.Length>0){b.AppendLine(n.Note);b.AppendLine();}if(n.Kind=="circle")Walk(n.Id,level+1);
            }
        }
        Walk(null,2);b.AppendLine("## Relations\n");
        foreach(var e in Edges)
        {
            var title=string.IsNullOrWhiteSpace(e.Caption)?"":e.Caption+": ";
            b.AppendLine("- "+title+Name(Node(e.From))+" ["+GraphStyle.RelationName(e.Label)+"] "+(e.Direction=="both"?"↔":e.Direction=="reverse"?"←":"→")+" "+Name(Node(e.To)));
            if(e.Note.Length>0){b.AppendLine();foreach(var line in e.Note.Replace("\r\n","\n").Split('\n'))b.AppendLine("    "+line);b.AppendLine();}
        }
        b.AppendLine("\n## □\n");foreach(var r in Regions){b.AppendLine("### "+r.Title);if(r.Note.Length>0)b.AppendLine(r.Note);if(!r.IsAbsolute)b.AppendLine(string.Join("; ",r.Members.Select(id=>Name(Node(id)))));b.AppendLine();}return b.ToString();
    }
    public static GraphDocument Demo()=>new(){Title="How does writing help us think?",Nodes=[
        new(){Id="a",Title="Writing helps us develop understanding, as well as record conclusions.",Note="This is the central proposition of the example paper.\n\nReplace it with a claim from your own research. Use this space for context, sources and examples. Notes do not change the size of the point.",X=435,Y=195,Color="#719AC7"},
        new(){Id="b",Title="Putting an idea into words can reveal hidden assumptions.",Note="Add an explanation, source or specific example here.",X=65,Y=85,Color="#73A899"},
        new(){Id="c",Title="Relations between propositions make an argument easier to follow.",X=65,Y=345,Color="#73A899"},
        new(){Id="d",Title="An overly complex structure can interrupt thinking.",Note="Keep objections alongside a claim to clarify its limits.",X=785,Y=355,Color="#D1A07E"},
        new(){Id="round",Kind="circle",Title="From loose ideas\nto a complete argument",Note="A ◎ encloses a connected graph in an open ring. Its contents remain visible, and the ring itself can be connected to other objects.\n\nDouble-click to enter its board. Press Space to show or hide graph titles.",X=825,Y=55,Color="#A291C9"},
        new(){Id="i1",Parent="round",Title="Write down your initial intuition.",X=120,Y=180,Color="#A291C9"},
        new(){Id="i2",Parent="round",Title="Fill in the steps between premises and conclusion.",X=455,Y=180,Color="#719AC7"},
        new(){Id="i3",Parent="round",Title="Check whether each step supports the next.",X=790,Y=180,Color="#73A899"}],
        Edges=[new(){Id="e1",From="b",To="a",Label="Support"},new(){Id="e2",From="c",To="a",Label="Support"},new(){Id="e3",From="d",To="a",Label="Qualification"},new(){Id="e4",From="i3",To="a",Label="Support"},new(){Id="e5",From="i1",To="i2",Label="Inference"},new(){Id="e6",From="i2",To="i3",Label="Inference"}],
        Regions=[new(){Id="r1",Title="Two roles of writing",Members=["b","c"]}]
    };
}

public static class Storage
{
    public static string CompatiblePath(string directory,string name,string legacyName)
    {
        var path=Path.Combine(directory,name);return File.Exists(path)?path:Path.Combine(directory,legacyName);
    }
    public static void Save(string path,GraphDocument document)
    {
        document.Validate();Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+".tmp";
        using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {var bytes=Encoding.UTF8.GetBytes(document.Serialize());f.Write(bytes);f.Flush(true);}
        if(File.Exists(path))File.Replace(temp,path,path+".bak",true);else File.Move(temp,path);
    }
}

public static class ModelTests
{
    public static void Run()
    {
        void Assert(bool b,string m){if(!b)throw new Exception(m);}
        var d=GraphDocument.Demo();d.Validate();Assert(d.Visible(null).Count==5,"Visible count");Assert(d.VisibleEdges(null).Any(e=>e.From=="round"&&e.To=="a"),"Projected external edge");
        var original=d.Serialize();var c=d.Collapse(["b","a","c"],null);d.Validate();Assert(d.Edges.Any(e=>e.From=="b"&&e.To=="a"),"Internal endpoint preserved");Assert(d.VisibleEdges(null).Any(e=>e.From=="d"&&e.To==c.Id),"External endpoint projection");Assert(d.Visible(c.Id).Count==3,"Inside scope");d.Dissolve(c.Id);d.Validate();Assert(d.Serialize()==original,"Collapse dissolve roundtrip");
        var partial=d.Collapse(["b","a"],null);d.Validate();Assert(d.Regions.Single().Members.Contains(partial.Id),"Partial region projects circle");d.Dissolve(partial.Id);d.Validate();Assert(d.Regions.Single().Members.ToHashSet().SetEquals(["b","c"]),"Partial region membership restored");
        bool rejected=false;try{d.Collapse(["b","d"],null);}catch(InvalidOperationException){rejected=true;}Assert(rejected,"Disconnected selection rejected");
        var nested=d.Collapse(["a","round"],null);d.Validate();Assert(d.Project("i1",null)==nested.Id,"Nested projection");d.DeleteNodes([nested.Id]);d.Validate();Assert(d.Node("i1")==null,"Descendant deletion");
        var bad=GraphDocument.Demo();bad.Node("round")!.Parent="round";rejected=false;try{bad.Validate();}catch(InvalidDataException){rejected=true;}Assert(rejected,"Cycle rejected");
        var split=new GraphDocument{Nodes=[new(){Id="text",Title="Original proposition\nSecond line",Note="Original notes\nLast line"},new(){Id="note",Title="",Note="Notes only"},new(){Id="circle",Kind="circle",Title="Group proposition",Note="Group notes"}],Regions=[new(){Id="box",IsAbsolute=true,Title="Frame proposition",Note="Frame notes"}]};
        var splitJson=split.Serialize();Assert(GraphDocument.Parse(splitJson).Serialize()==splitJson,"Separate proposition and notes survive persistence without merging");
        var root=Path.Combine(Path.GetTempPath(),"yujian-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var file=Path.Combine(root,"test.yujian");d=GraphDocument.Demo();Storage.Save(file,d);d.Title="中文保存 ✓";Storage.Save(file,d);Assert(GraphDocument.Parse(File.ReadAllText(file)).Title==d.Title,"Unicode disk persistence");Assert(GraphDocument.Parse(File.ReadAllText(file+".bak")).Title!=d.Title,"Backup preserved");Assert(d.Markdown().Contains("Relations"),"Markdown export");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: projection, connected collapse, lossless dissolve, nested scopes, deletion, cycle validation, Unicode persistence, atomic backup, Markdown export.\n");
    }
}


