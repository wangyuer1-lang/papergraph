using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Papergraph;

public static class ArrangeTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static Proposition N(string id,double x,double y,string? parent=null)=>new(){Id=id,Title="Proposition "+id,Caption=id,Note="keep note",MarkColor="#E5484D",X=x-125,Y=y-60,Parent=parent};
    static Region R(string id,double x,double y,double w,double h,string? parent=null)=>new(){Id=id,Title=id,IsAbsolute=true,X=x,Y=y,Width=w,Height=h,Parent=parent};
    static Relation E(string from,string to,string kind="Inference")=>new(){From=from,To=to,Label=kind,Note="keep relation note"};
    static void Apply(GraphDocument doc,GraphArrange.Result result){foreach(var (id,p) in result.Positions){doc.Node(id)!.X=p.X;doc.Node(id)!.Y=p.Y;}}
    static void Wait(Task task)
    {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(5)};timer.Tick+=(s,e)=>{if(task.IsCompleted)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();task.GetAwaiter().GetResult();
    }
    public static void Run()
    {
        var doc=new GraphDocument{Nodes=[N("a",100,100),N("b",150,300),N("c",300,520),N("x",1200,100),N("y",1300,300),N("out",2500,80)],Regions=[R("A",0,0,850,1000),R("B",1000,0,850,1000)],Edges=[E("a","b"),E("b","c"),E("x","y"),E("a","y"),E("c","x")]};
        var before=doc.Serialize();var result=GraphArrange.Compute(doc,null,null,[],[]);Check(result.Positions.Count>=3,"Arrange must actually improve roomy frame layout");Apply(doc,result);
        Check(doc.Regions[0].X==0&&doc.Regions[1].X==1000,"Global tidy keeps frame positions fixed");
        Check(GraphBoard.Members(doc,doc.Regions[0]).Select(n=>n.Id).ToHashSet().SetEquals(["a","b","c"]),"Global tidy preserves first frame");
        Check(GraphBoard.Members(doc,doc.Regions[1]).Select(n=>n.Id).ToHashSet().SetEquals(["x","y"]),"Global tidy preserves second frame");
        Check(GraphStyle.Center(doc.Node("out")!)==new Point(2500,80),"Global tidy leaves unframed objects fixed");
        var without=GraphDocument.Parse(before);without.Edges.RemoveAll(e=>(e.From=="a"&&e.To=="y")||(e.From=="c"&&e.To=="x"));
        var independent=GraphArrange.Compute(without,null,null,[],[]);Check(result.Positions.OrderBy(p=>p.Key).SequenceEqual(independent.Positions.OrderBy(p=>p.Key)),"Cross-frame links never attract nodes or affect ordering");
        Check(GraphArrange.Compute(doc,null,null,[],[]).Positions.Count==0,"Repeating tidy is stable");
        var only=GraphDocument.Parse(before);var selected=GraphArrange.Compute(only,null,null,[],["A"]);Check(selected.Positions.Keys.All(id=>id is "a" or "b" or "c"),"Selected frame isolates tidy");
        var overlap=new GraphDocument{Nodes=[N("left",100,120),N("i",420,150),N("j",460,350),N("right",800,180)],Regions=[R("L",0,0,650,700),R("R",350,0,650,700)],Edges=[E("left","i"),E("i","j"),E("j","right")]};
        var sets=overlap.Regions.ToDictionary(r=>r.Id,r=>GraphBoard.Members(overlap,r).Select(n=>n.Id).ToHashSet());Apply(overlap,GraphArrange.Compute(overlap,null,null,[],[]));
        foreach(var r in overlap.Regions)Check(sets[r.Id].SetEquals(GraphBoard.Members(overlap,r).Select(n=>n.Id)),"Overlapping frames retain exact shared membership");
        var group=new Proposition{Id="ring",Kind="circle",Expanded=true,Caption="ring",X=900,Y=900};
        var nested=new GraphDocument{Nodes=[group,N("p",260,270,"ring"),N("q",520,460,"ring"),N("r",340,650,"ring"),N("other",1400,500)],Regions=[R("wrapper",0,0,1800,1300),R("inner",100,100,850,850)],Edges=[E("p","q"),E("q","r"),E("p","other")]};
        var initialRing=GraphGroups.Bounds(nested,group);var other=GraphStyle.Center(nested.Node("other")!);var nestedResult=GraphArrange.Compute(nested,null,null,["ring"],[]);Apply(nested,nestedResult);
        Check(Math.Abs(initialRing.Width-GraphGroups.Bounds(nested,group).Width)<.02,"Selected ring retains its visible diameter instead of compacting");Check(GraphStyle.Center(nested.Node("other")!)==other,"Selected ring does not move outside points");
        Check(nested.Nodes.Where(n=>n.Parent=="ring").All(n=>GraphBoard.Bounds(nested.Regions[1]).Contains(GraphStyle.Bounds(n))),"Expanded ring descendants stay inside their frame");
        var pair=new GraphDocument{Nodes=[new Proposition{Id="pair",Kind="circle",Expanded=true},N("pa",300,300,"pair"),N("pb",700,700,"pair")],Regions=[R("pair-frame",0,0,1200,1200)],Edges=[E("pa","pb")]};
        Apply(pair,GraphArrange.Compute(pair,null,null,[],[]));Check(GraphArrange.Compute(pair,null,null,[],[]).Positions.Count==0,"Repeated tidy never rotates or progressively shrinks a two-point ring");
        var cycle=new GraphDocument{Nodes=[N("1",100,100),N("2",350,160),N("3",180,320)],Regions=[R("cycle",0,0,1000,1000)],Edges=[E("1","2"),E("2","3"),E("3","1")]};
        Apply(cycle,GraphArrange.Compute(cycle,null,null,[],[]));Check(cycle.Nodes.All(n=>double.IsFinite(n.X)&&double.IsFinite(n.Y)),"Cycles produce finite bounded positions");
        Check(GraphArrange.Compute(cycle,null,null,[],[]).Positions.Count==0,"Cyclic layout remains stable on repeated clicks");
        var tight=new GraphDocument{Nodes=[N("one",30,30),N("two",45,45)],Regions=[R("tight",0,0,80,80)],Edges=[E("one","two")]};
        var tightResult=GraphArrange.Compute(tight,null,null,[],[]);Check(tightResult.Positions.Count==0&&tightResult.Crowded==1,"Too-small frame is preserved instead of ejecting or overlapping points");
        var directed=new GraphDocument{Nodes=[N("end",100,100),N("middle",400,150),N("start",700,200)],Regions=[R("wide",0,0,1600,360)],Edges=[E("start","middle","Qualification"),E("end","middle","Opposition")]};
        directed.Edges[1].Direction="reverse";Apply(directed,GraphArrange.Compute(directed,null,null,[],[]));
        Check(directed.Node("start")!.X<directed.Node("middle")!.X&&directed.Node("middle")!.X<directed.Node("end")!.X,"All single-headed relation glyphs follow the true arrow direction, including reverse arrows");
        Check(directed.Node("end")!.X-directed.Node("start")!.X>1200,"Roomy frames distribute a directed chain across their usable width");
        var branch=new GraphDocument{Nodes=[N("source",150,150),N("right",500,500),N("left",600,200),N("sink",800,400)],Regions=[R("branch",0,0,1800,1000)],Edges=[E("source","left"),E("source","right"),E("left","sink"),E("right","sink")]};
        Apply(branch,GraphArrange.Compute(branch,null,null,[],[]));
        Check(branch.Node("source")!.X<branch.Node("left")!.X&&branch.Node("source")!.X<branch.Node("right")!.X&&branch.Node("left")!.X<branch.Node("sink")!.X&&branch.Node("right")!.X<branch.Node("sink")!.X,"Branch and merge retain a left-to-right layered reading order");
        Check(Math.Abs(branch.Node("left")!.X-branch.Node("right")!.X)<.01,"Parallel branches share a layer");
        foreach(var edge in branch.Edges)Check((GraphStyle.Center(branch.Node(edge.From)!)-GraphStyle.Center(branch.Node(edge.To)!)).Length>=GraphStyle.MinimumNodeDistance(GraphStyle.Radius(branch.Node(edge.From)!),GraphStyle.Radius(branch.Node(edge.To)!))-.1,"Directed layout retains a readable arrow shaft");
        Check(GraphArrange.Compute(branch,null,null,[],[]).Positions.Count==0,"Branch layout is stable on repeated clicks");
        foreach(var n in doc.Nodes)Check(n.Title=="Proposition "+n.Id&&n.Note=="keep note"&&n.MarkColor=="#E5484D","Layout changes coordinates only");
        Check(doc.Edges.All(e=>e.Note=="keep relation note"),"Relation content survives layout");
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-arrange-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);Storage.Save(Path.Combine(directory,"Example.papergraph"),GraphDocument.Parse(before));
        var window=new MainWindow(directory);var graph=window.Graph;var saved=graph.Document.Serialize();
        graph.SetView(.35,new Point(72,96));var viewZoom=graph.Zoom;var viewOffset=graph.Offset;
        Wait(graph.ArrangeInsideAsync());var laidOut=graph.Document.Serialize();Check(saved!=laidOut,"Native arrange applies result");
        Check(graph.Zoom==viewZoom&&graph.Offset==viewOffset,"Arrange never zooms out or moves the viewport");
        var undo=window.EditMenu.Items.OfType<MenuItem>().First(i=>i.Header?.ToString()=="Undo");undo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(graph.Document.Serialize()==saved,"Native arrange is one undo step");
        var redo=window.EditMenu.Items.OfType<MenuItem>().First(i=>i.Header?.ToString()=="Redo");redo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(graph.Document.Serialize()==laidOut,"Native arrange redo restores exact layout");
        graph.Document=GraphDocument.Parse(before);var task=graph.ArrangeInsideAsync();graph.SetBoard(null,"A");Wait(task);Check(graph.Document.Serialize()==before,"Changing board cancels pending arrangement");window.Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: bounded per-frame directional layout, ring isolation, intersections, nested membership, cross-frame independence, cycles, crowded-frame fallback, repeat stability, native undo/redo and cancellation.\n");
    }
}
