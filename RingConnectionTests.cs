using System.IO;
using System.Reflection;
using System.Windows;
namespace Papergraph;

public static class RingConnectionTests
{
    public static void Run()
    {
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        var doc=new GraphDocument{Nodes=[new(){Id="a",X=0,Y=100},new(){Id="b",X=120,Y=100},new(){Id="c",X=800,Y=100},new(){Id="d",X=920,Y=100},new(){Id="p",X=400,Y=600}],Edges=[new(){From="a",To="b"},new(){From="c",To="d"}]};
        var left=doc.Collapse(["a","b"],null);var right=doc.Collapse(["c","d"],null);
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-ring-links-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);Storage.Save(Path.Combine(directory,"Example.papergraph"),doc);
        var window=new MainWindow(directory);var graph=window.Graph;graph.Measure(new Size(1300,900));graph.Arrange(new Rect(0,0,1300,900));
        left=graph.Document.Node(left.Id)!;right=graph.Document.Node(right.Id)!;
        foreach(var zoom in new[]{.2,1d,2d})
        {
            graph.SetView(zoom,new Point());var box=graph.ObjectBounds(left);var midY=box.Y+box.Height/2;
            Check(graph.ConnectionTarget(new Point(box.Right+12/zoom,midY),"p")?.Id==left.Id,"Ring connection port has a constant screen-space target at every zoom");
            Check(graph.ConnectionTarget(new Point(box.X+box.Width/2,box.Top-12/zoom),"p")?.Id==left.Id,"The entire ring boundary accepts connections");
            Check(graph.ConnectionTarget(GraphStyle.Center(graph.Document.Node("a")!),"p")?.Id=="a","Connecting to a member does not redirect to its ring");
            Check(graph.ConnectionTarget(new Point(box.Right,midY),left.Id)==null,"A ring cannot connect to itself");
        }
        var positions=graph.Document.Nodes.ToDictionary(n=>n.Id,GraphStyle.Center);int layouts=0;graph.LayoutFinished+=()=>layouts++;
        var add=typeof(MainWindow).GetMethod("AddEdge",BindingFlags.Instance|BindingFlags.NonPublic)!;
        foreach(var pair in new[]{(left.Id,"p"),("p",right.Id),(left.Id,right.Id)})add.Invoke(window,[pair.Item1,pair.Item2]);
        Check(graph.VisibleLinks.Contains((left.Id,"p"))&&graph.VisibleLinks.Contains(("p",right.Id))&&graph.VisibleLinks.Contains((left.Id,right.Id)),"Ring-to-point, point-to-ring and ring-to-ring relations retain the ring IDs as endpoints");
        Check(layouts==0&&positions.All(p=>GraphStyle.Center(graph.Document.Node(p.Key)!)==p.Value),"Connecting rings never rearranges their contents or other points");
        graph.SetView(1,new Point());typeof(GraphSurface).GetMethod("BuildGeometry",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(graph,null);
        var paths=(System.Collections.IEnumerable)typeof(GraphSurface).GetField("paths",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(graph)!;
        Check(paths.Cast<object>().Count()==5,"All original and ring relations produce visible edge geometry");
        var restored=GraphDocument.Parse(graph.Document.Serialize());
        Check(restored.Edges.Any(e=>e.From==left.Id&&e.To==right.Id),"Saving preserves direct ring endpoints");
        window.Close();File.AppendAllText(TestEvidence.ResultsPath,"PASS: ring boundary/port connection targets at multiple zooms, independent member endpoints, point/ring and ring/ring links, rendered geometry, stable layout and saved endpoints.\n");
    }
}
