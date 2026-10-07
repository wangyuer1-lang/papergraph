using System.IO;
using System.Windows;
namespace Papergraph;

public static class CaptionLayoutTests
{
    public static void Run()
    {
        void Check(bool pass,string reason){if(!pass)throw new Exception(reason);}
        var viewport=new Rect(0,0,700,600);var center=new Point(250,200);var text=new Size(90,22);
        var scene=new CaptionLayout(viewport);scene.AddCircle(center,23);scene.AddLine(new Point(250,225),new Point(250,500));
        var nodeCandidates=GraphCaptions.NodeCandidates("node",text,center,20,1).ToArray();var node=scene.Choose(nodeCandidates);
        Check(node.Placement.Bounds.Bottom<center.Y&&scene.Score(node.Placement)==0,"A point title moves above a downward arrow instead of covering it");
        var heads=new CaptionLayout(viewport);heads.AddCircle(center,23);heads.AddLine(new Point(290,236),new Point(310,236),11,850);
        Check(heads.Score(heads.Choose(nodeCandidates).Placement)==0&&heads.Choose(nodeCandidates).Slot!=0,"A nearby arrowhead is avoided even when its shaft misses the title");
        var labels=new CaptionLayout(viewport);labels.AddTitle(nodeCandidates[0].Placement);var next=labels.Choose(nodeCandidates);
        Check(!next.Placement.Bounds.IntersectsWith(nodeCandidates[0].Placement.Bounds),"Neighboring titles use separate available positions");
        var crossing=new CaptionLayout(viewport);crossing.AddLine(new Point(100,200),new Point(600,200));crossing.AddLine(new Point(200,180),new Point(450,180));
        var edge=crossing.Choose(GraphCaptions.EdgeCandidates("edge",text,[(new Point(300,200),new Vector(1,0))],1));
        Check(edge.Placement.TextBounds.Top>0&&crossing.Score(edge.Placement)==0,"A relation title switches to the other side to avoid a crossing arrow");
        var nearby=new CaptionLayout(viewport);nearby.AddCircle(center,23);Check(nearby.Choose(nodeCandidates,node.Slot).Slot==node.Slot,"A still-suitable position is retained to prevent label jitter");
        Check(nearby.Choose(nodeCandidates,23).Slot==23,"Even a farther clear position stays anchored instead of jumping to the preferred default");
        var minor=new CaptionLayout(viewport);minor.AddLine(new Point(293,237),new Point(294,237));
        Check(minor.Choose(nodeCandidates,0).Slot==0,"A small incidental overlap does not cause a title to jump");
        Check(scene.Choose(nodeCandidates,0,false).Slot==0,"Camera movement preserves the chosen slot even when another slot scores better");
        Check(scene.Choose(nodeCandidates,0).Slot!=0,"A substantial new obstruction can still move a title to a clear position");
        var corner=new CaptionLayout(viewport);var nearEdge=corner.Choose(GraphCaptions.NodeCandidates("boundary",text,new Point(350,590),12,1));Check(viewport.Contains(nearEdge.Placement.Bounds),"Titles prefer a position inside the visible canvas");
        var packed=new CaptionLayout(viewport);packed.AddCircle(center,1000);var fallback=packed.Choose(nodeCandidates);Check(fallback.Placement.Id=="node"&&packed.Score(fallback.Placement)>0,"An unavoidable overlap uses the least obstructed candidate without hiding text");

        var doc=new GraphDocument{Nodes=[new(){Id="a",X=175,Y=100,Caption="Top"},new(){Id="b",X=175,Y=400,Caption="Bottom"}],Edges=[new(){Id="ab",From="a",To="b",Caption="Supports",Direction="both"}]};
        var graph=new GraphSurface{Document=doc,ShowCaptions=true};graph.Measure(new Size(700,600));graph.Arrange(viewport);graph.SetView(1,new Point());var snapshot=doc.Serialize();
        var first=graph.LayoutCaptions();Check(first.Count==3&&first.Single(p=>p.Id=="a").Bounds.Bottom<160,"The real graph routes a point title away from a bidirectional edge");
        graph.Selected=["a"];var selected=graph.LayoutCaptions();Check(first.SequenceEqual(selected),"Selecting a point does not reshuffle title positions");
        graph.SetView(1,new Point(7,9));var panned=graph.LayoutCaptions();foreach(var label in first){var expected=label.Bounds;expected.Offset(7,9);Check(panned.Single(p=>p.Id==label.Id).Bounds==expected,"Panning preserves title placement relative to its object");}
        var actual=panned.Single(p=>p.Id=="a");var hit=new Point(actual.Bounds.X+actual.Bounds.Width/2,actual.Bounds.Y+actual.Bounds.Height/2);Check(graph.HitNode(graph.ToWorld(hit))?.Id=="a","Repositioned point titles remain clickable");
        graph.RefreshData();Check(graph.LayoutCaptions().SequenceEqual(panned),"Refreshing graph data preserves title anchors");
        graph.SetView(1,new Point(7,-95));var boundary=graph.LayoutCaptions().Single(p=>p.Id=="a");
        Check(boundary.TextBounds==actual.TextBounds,"Panning near a viewport boundary does not flip title sides");
        graph.SetView(.25,new Point(200,100));Check(graph.LayoutCaptions().Count==3&&graph.LayoutCaptions().All(p=>p.Scale>=1),"Crowded overview keeps all titles at their readable minimum");
        foreach(var zoom in new[]{.15,.5,1.0,2.0,.25,1.0}){graph.SetView(zoom,new Point(200,100));graph.LayoutCaptions();}
        graph.SetView(1,new Point(7,9));Check(graph.LayoutCaptions().SequenceEqual(panned),"Repeated zoom round trips preserve all title sides and edge anchors");
        graph.ShowCaptions=false;graph.LayoutCaptions();graph.ShowCaptions=true;graph.ApplyTheme(true);Check(graph.LayoutCaptions().SequenceEqual(panned),"Title toggles and theme changes do not reshuffle positions");
        Check(doc.Serialize()==snapshot,"Title placement never changes graph positions or saved content");
        File.AppendAllText(TestEvidence.ResultsPath,"PASS: title placement avoids arrow shafts and heads, nearby titles and canvas boundaries, switches edge sides, retains stable positions and hit targets, and keeps all titles visible when collisions are unavoidable.\n");
    }
}
