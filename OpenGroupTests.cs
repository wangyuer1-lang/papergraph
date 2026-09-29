using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Papergraph;

public static class OpenGroupTests
{
    public static void Run()
    {
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        Proposition Node(string id,double x,double y)=>new(){Id=id,Caption=id,Title="Proposition "+id,Note="Note "+id,X=x-125,Y=y-60};
        var doc=new GraphDocument{Nodes=[Node("a",160,180),Node("b",320,180),Node("c",460,330),Node("outside",720,330)],Edges=[new(){Id="ab",From="a",To="b"},new(){Id="bc",From="b",To="c"},new(){Id="co",From="c",To="outside"}]};
        var initial=doc.Nodes.ToDictionary(n=>n.Id,GraphStyle.Center);var edgeJson=JsonSerializer.Serialize(doc.Edges);
        Check(!GraphGroups.CanGroup(doc,["a","outside"]),"Disconnected points cannot create a ring");
        var group=doc.Collapse(["a","b"],null);group.Caption="Inner argument";group.Note="Group source";
        Check(group.Expanded&&initial.All(p=>GraphStyle.Center(doc.Node(p.Key)!)==p.Value),"Creating a ring keeps every original point in place");
        var graph=new GraphSurface{Document=doc};graph.Measure(new Size(1000,750));graph.Arrange(new Rect(0,0,1000,750));graph.SetView(1,new Point());
        Check(graph.VisibleNodes.Count==5&&graph.VisibleLinks.Count()==3,"Overview exposes group contents and original internal/external relations");
        Check(graph.HitNode(GraphStyle.Center(doc.Node("a")!))?.Id=="a","Group contents take priority over the enclosing ring");
        var box=graph.ObjectBounds(group);var top=new Point(box.X+box.Width/2,box.Top);
        Check(graph.HitNode(top)?.Id==group.Id&&graph.HitNode(new Point(240,180))==null,"Only the ring boundary is selectable; its empty interior remains a canvas");
        var oldBox=box;doc.Node("b")!.X+=70;graph.ContentChanged();Check(graph.ObjectBounds(group).Width>oldBox.Width,"Moving a member expands the enclosing ring");
        doc.Regions.Add(new(){Id="inner-frame",Parent=group.Id,IsAbsolute=true,X=120,Y=130,Width=320,Height=110});
        var positions=doc.Nodes.ToDictionary(n=>n.Id,GraphStyle.Center);new SelectionMove(doc,[group.Id,"a"],[]).Apply(new Vector(30,25));
        Check(GraphStyle.Center(doc.Node("a")!)==positions["a"]+new Vector(30,25)&&GraphStyle.Center(doc.Node("b")!)==positions["b"]+new Vector(30,25),"Selecting a ring and its child moves each member once");
        Check(GraphStyle.Center(doc.Node("outside")!)==positions["outside"]&&doc.Regions.Single().X==150,"Group movement includes its owned frames but not external nodes");
        Check(GraphGroups.CanGroup(doc,[group.Id,"a","b","c"]),"A connected group and another point can form a nested ring without double-counting descendants");
        var outer=doc.Collapse([group.Id,"c"],null);outer.Caption="Whole argument";outer.Note="Outer note";
        graph.RefreshData();Check(graph.VisibleNodes.Count==6&&graph.VisibleLinks.Count()==3,"Nested rings remain expanded in the overview");
        Check(GraphGroups.Bounds(doc,outer).Contains(GraphGroups.Bounds(doc,group)),"An outer ring encloses its inner ring");
        foreach(var zoom in new[]{.2,1d,2d})
        {
            graph.SetView(zoom,new Point(20,20));var bounds=graph.ObjectBounds(outer);
            foreach(var id in new[]{"a","b","c"})Check(bounds.Contains(GraphStyle.Center(doc.Node(id)!)),"All descendants remain inside the ring at every zoom");
        }
        graph.SetView(1,new Point());graph.ApplyTheme(true);graph.ShowCaptions=true;graph.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1000,750,96,96,PixelFormats.Pbgra32);bitmap.Render(graph);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"open-groups-preview.png")))encoder.Save(stream);
        var beforeUngroup=doc.Nodes.Where(n=>n.Id!=outer.Id).ToDictionary(n=>n.Id,GraphStyle.Center);doc.Dissolve(outer.Id);
        Check(beforeUngroup.All(p=>GraphStyle.Center(doc.Node(p.Key)!)==p.Value)&&doc.Node(outer.Id)?.Note=="Outer note"&&doc.Node(outer.Id)?.Kind=="point","Ungroup leaves contents in place and retains the group's writing as a point");
        Check(JsonSerializer.Serialize(doc.Edges)==edgeJson,"Grouping, movement and ungrouping preserve edge identities and endpoints");
        var legacy=new GraphDocument{Nodes=[new(){Id="legacy",Kind="circle",X=822,Y=222,Note="Legacy notes"},Node("l1",100,100),Node("l2",300,100)],Edges=[new(){From="l1",To="l2"}]};legacy.Node("l1")!.Parent=legacy.Node("l2")!.Parent="legacy";
        Check(GraphGroups.ExpandLegacy(legacy),"Legacy collapsed groups are migrated");var center=(GraphStyle.Center(legacy.Node("l1")!)+((GraphStyle.Center(legacy.Node("l2")!)-GraphStyle.Center(legacy.Node("l1")!))*.5));
        Check((center-GraphStyle.Center(legacy.Node("legacy")!)).Length<.001,"Legacy contents appear at the formerly moved collapsed point");
        var migrated=legacy.Serialize();Check(!GraphGroups.ExpandLegacy(legacy)&&legacy.Serialize()==migrated,"Migration is idempotent and does not drift on reopen");
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-open-group-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);Storage.Save(Path.Combine(directory,"Example.papergraph"),legacy);
        var window=new MainWindow(directory);window.Graph.Selected=["legacy"];window.SelectionChanged();Check(window.NotesBox.Text=="Legacy notes","The ring opens its own notes in the existing right panel");
        var response=JsonSerializer.SerializeToElement(window.HandleAgent(JsonSerializer.SerializeToElement(new{operation="snapshot"})),GraphDocument.Options);
        Check(response.GetProperty("groups")[0].GetProperty("memberIds").GetArrayLength()==2&&response.GetProperty("visibleNodeIds").GetArrayLength()==3,"Agents see expanded group membership and visible children");
        window.Enter("legacy");Check(window.Graph.VisibleNodes.Count==2,"Double-click focus still isolates a group's contents");window.Leave();Check(window.Graph.VisibleNodes.Count==3,"Leaving focus restores the expanded overview");window.Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: open connected rings, visible child nodes/edges, boundary picking, responsive bounds, nested membership, deduplicated group drag, notes, lossless ungroup, legacy migration, agent snapshots and focused boards.\n");
    }
}
