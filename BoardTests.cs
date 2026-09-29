using System.IO;
using System.Windows;
namespace Papergraph;

public static class BoardTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static Proposition Node(string id,double x,double y)=>new(){Id=id,X=x-125,Y=y-60,Title=id};
    public static void Run()
    {
        var a=new Region{Id="A",Title="A",IsAbsolute=true,Color="#618FBC",X=0,Y=0,Width=400,Height=300};
        var b=new Region{Id="B",Title="B",IsAbsolute=true,Color="#579A88",X=200,Y=0,Width=400,Height=300};
        var inner=new Region{Id="inner",IsAbsolute=true,X=20,Y=160,Width=70,Height=60};
        var doc=new GraphDocument{Nodes=[Node("a",80,80),Node("ab",300,80),Node("b",530,80),Node("out",750,80)],Regions=[a,b,inner],Edges=[new(){From="a",To="ab"},new(){From="ab",To="b"},new(){From="b",To="out"}]};
        doc.Validate();var own=doc.Node("ab")!.Color;var color=GraphRegionColors.ForNode(doc.Node("ab")!,doc.Regions);
        Check(GraphRegionColors.ForNode(doc.Node("a")!,doc.Regions)==a.Color&&GraphRegionColors.ForNode(doc.Node("b")!,doc.Regions)==b.Color,"Members adopt containing frame color");
        Check(color!=a.Color&&color!=b.Color&&GraphRegionColors.Combined([a,b])==GraphRegionColors.Combined([b,a]),"Intersection color is new and order-independent");
        Check(GraphRegionColors.ForNode(doc.Node("out")!,doc.Regions)==doc.Node("out")!.Color,"Outside color remains personal");
        var reopened=GraphDocument.Parse(doc.Serialize());Check(GraphRegionColors.ForNode(reopened.Node("ab")!,reopened.Regions)==color,"Intersection color survives reopen");
        Check(GraphRegionColors.Intersections(doc.Regions).Any(p=>p.Bounds.Contains(new Point(300,80))&&p.Color==color),"Intersection area agrees with node color");
        var priorPositions=doc.Nodes.ToDictionary(n=>n.Id,GraphStyle.Center);new RegionMove(doc,a,false).Apply(new Vector(-450,0));
        Check(b.X==200&&doc.Nodes.All(n=>GraphStyle.Center(n)==priorPositions[n.Id]),"Moving one intersecting frame never moves the other frame or shared points");
        Check(GraphRegionColors.ForNode(doc.Node("ab")!,doc.Regions)==b.Color&&doc.Node("ab")!.Color==own,"Leaving intersection restores remaining frame color without overwriting personal color");
        a.X=0;var movement=new RegionMove(doc,a,true);movement.Apply(new Vector(-200,10));movement.Apply(new Vector(-220,20));
        Check(GraphStyle.Center(doc.Node("ab")!)==priorPositions["ab"]+new Vector(-220,20)&&b.X==200&&GraphStyle.Center(doc.Node("b")!)==priorPositions["b"],"Group drag includes shared member once and leaves intersecting frame independent");
        Check(inner.X==-200&&inner.Y==180,"Contained threshold moves with its group");
        movement.Apply(new Vector());Check(doc.Nodes.All(n=>GraphStyle.Center(n)==priorPositions[n.Id])&&inner.X==20,"Movement uses original positions without accumulated drift");
        var graph=new GraphSurface{Document=doc};graph.SetBoard(null,a.Id);
        Check(graph.VisibleNodes.Select(n=>n.Id).ToHashSet().SetEquals(["a","ab"]),"Threshold board isolates contained nodes");
        Check(graph.VisibleLinks.Count()==1&&graph.VisibleRegions.Count==1&&graph.VisibleRegions[0].Id==inner.Id,"Threshold board hides external edges and overlapping noncontained frames");
        Check(graph.ClampToBoard(new Point(-300,900)).X>=0&&graph.ClampToBoard(new Point(-300,900)).Y<=300,"New nodes remain inside active board");
        graph.SetBoard(null,null);var coincident=new Region{Id="same",IsAbsolute=true,X=0,Y=0,Width=400,Height=300};doc.Regions.Add(coincident);graph.RefreshData();
        foreach(var zoom in new[]{.10,.25,1d,2d}){graph.SetView(zoom,new Point());var grips=graph.RegionGrips();Check((grips[a.Id]-grips[coincident.Id]).Length*zoom>=23,"Coincident frames retain separate screen-sized handles");Check(graph.HitRegion(grips[a.Id])?.Id==a.Id&&graph.HitRegion(grips[coincident.Id])?.Id==coincident.Id,"Either coincident frame can be independently picked");}
        doc.Regions.Remove(coincident);
        var circle=doc.Collapse(["a","ab"],null);circle.Title="集成命题";
        var directory=Path.Combine(Path.GetTempPath(),"yujian-boards-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);Storage.Save(Path.Combine(directory,"Example.papergraph"),doc);
        var window=new MainWindow(directory);window.Graph.SetView(.4,new Point(32,74));var original=window.Graph.Document.Serialize();window.EnterRegion(a.Id);window.Graph.SetView(1.1,new Point(67,84));window.Graph.Selected=[circle.Id];window.RouteTitleToggle(System.Windows.Input.Key.Space,System.Windows.Input.ModifierKeys.None,window.Graph);
        Check(window.Graph.BoardRegion==a.Id&&window.Graph.Zoom==1.1&&window.Graph.Offset==new Point(67,84),"Space toggles titles without entering the selected double ring or moving the board");
        window.Enter(circle.Id);Check(window.Graph.Scope==circle.Id&&window.Graph.BoardRegion==null&&window.Graph.VisibleNodes.Count==2,"Double ring opens only its inner graph");window.Leave();
        Check(window.Graph.Scope==null&&window.Graph.BoardRegion==a.Id&&window.Graph.Zoom==1.1&&window.Graph.Offset==new Point(67,84),"Nested return restores threshold viewport");window.Leave();
        Check(window.Graph.Scope==null&&window.Graph.BoardRegion==null&&window.Graph.Zoom==.4&&window.Graph.Offset==new Point(32,74),"Threshold return restores original overview");Check(window.Graph.Document.Serialize()==original,"Board navigation never mutates content");window.Close();
        new SelectionMove(doc,[circle.Id],[]).Apply(new Vector(120,-35));var movedChildren=doc.Visible(circle.Id).ToDictionary(n=>n.Id,GraphStyle.Center);doc.Dissolve(circle.Id);Check(movedChildren.All(p=>GraphStyle.Center(doc.Node(p.Key)!)==p.Value),"Ungroup keeps the moved visible children exactly in place");
        var sim=new GraphRelaxation([new("seed",new Point(100,100),12,true,Seed:true),new("near",new Point(100,100),12,true),new("pinned",new Point(160,100),12,false)],[]);
        for(int i=0;i<110;i++)sim.Step();Check(sim.Finished&&sim.Positions.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)),"Local simulation cools and stays finite");
        Check((sim.Positions[0]-sim.Positions[1]).Length>35,"Coincident nodes gently separate");Check((sim.Positions[1]-new Point(100,100)).Length<=42.001&&sim.Positions[2]==new Point(160,100),"Neighbor motion bounded and dragged node pinned");
        var constrained=new GraphRelaxation([new("x",new Point(20,20),12,true,new Rect(0,0,80,80),true),new("y",new Point(20,20),12,true,new Rect(0,0,80,80),true)],[]);for(int i=0;i<110;i++)constrained.Step();Check(constrained.Positions.All(p=>new Rect(17,17,46,46).Contains(p)),"Simulation preserves frame membership");
        var global=new GraphRelaxation(Enumerable.Range(0,10).Select(i=>new GraphRelaxation.Body(i.ToString(),new Point(),12,true)).ToArray(),Enumerable.Range(0,9).Select(i=>(i.ToString(),(i+1).ToString())).ToArray(),false);for(int i=0;i<250;i++)global.Step();
        Check(global.Finished&&global.Positions.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)),"Global arrangement cools from coincident graph");Check(global.Positions.SelectMany((p,i)=>global.Positions.Skip(i+1).Select(q=>(p-q).Length)).Min()>24,"Global arrangement separates circles");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: threshold isolation, internal links only, nested circle/threshold return and title toggle, exact viewport restoration, independent intersecting frames, group move without drift, overlap handles at four zooms, membership coloring/restoration/persistence, intersection tint, bounded damped motion, pinned nodes, boundary constraints and cooling.\n");
    }
}

