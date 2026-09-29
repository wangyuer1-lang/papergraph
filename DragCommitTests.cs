using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
namespace Papergraph;

public static class DragCommitTests
{
    public static void Run()
    {
        void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-drag-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var doc=new GraphDocument{Nodes=[new(){Id="a",X=0,Y=0},new(){Id="b",X=300,Y=0},new(){Id="c",X=600,Y=0},new(){Id="near",X=80,Y=170}],Edges=[new(){From="a",To="b"},new(){From="b",To="c"}]};
        Storage.Save(Path.Combine(directory,"Example.papergraph"),doc);var window=new MainWindow(directory);var graph=window.Graph;
        graph.Measure(new Size(1000,700));graph.Arrange(new Rect(0,0,1000,700));graph.SetView(1,new Point());int layoutCommits=0;graph.LayoutFinished+=()=>layoutCommits++;
        void DragAndRelease(string[] ids,Vector delta)
        {
            graph.Selected=ids.ToHashSet();graph.SelectedRegions.Clear();var before=graph.Document.Nodes.ToDictionary(n=>n.Id,GraphStyle.Center);var moving=graph.Document.Descendants(ids);
            graph.BeginSelectionMove();graph.MoveSelection(delta);
            // Exercise the real mouse-up commit path without sending input to a user's window.
            typeof(GraphSurface).GetField("moved",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(graph,true);
            var atRelease=graph.Document.Serialize();graph.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});
            Check(graph.Document.Serialize()==atRelease,"Releasing a dragged selection preserves its exact final layout");
            Check(before.Where(p=>!moving.Contains(p.Key)).All(p=>GraphStyle.Center(graph.Document.Node(p.Key)!)==p.Value),"Unselected points never move during or after a drag");
            Check(layoutCommits==0,"Dropping a point or group never invokes automatic layout");
        }
        DragAndRelease(["a"],new Vector(-70,30));DragAndRelease(["a","b"],new Vector(-25,25));
        var group=graph.Document.Collapse(["a","b"],null);graph.RefreshData();DragAndRelease([group.Id],new Vector(-40,10));
        graph.Selected=["c"];graph.BeginSelectionMove();graph.MoveSelection(new Vector(-1000,0));graph.EndSelectionMove();
        Check((GraphStyle.Center(graph.Document.Node("c")!)-GraphStyle.Center(graph.Document.Node("b")!)).Length>=GraphStyle.MinimumNodeDistance(12,12)-.1,"The minimum connector gap still stops a point being dragged too close");
        window.Close();File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: real drag release preserves final positions for points, multiselections and open groups, leaves other nodes fixed, invokes no layout and retains minimum connected spacing.\n");
    }
}
