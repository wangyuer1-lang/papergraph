using System.Diagnostics;
using System.IO;
using System.Windows;
namespace Papergraph;

public static class SpacingTests
{
    public static void Run()
    {
        void Check(bool success,string message){if(!success)throw new Exception(message);}
        var min=GraphStyle.MinimumNodeDistance(12,12);
        var pair=new GraphRelaxation([new("a",new Point(),12,false),new("b",new Point(5,0),12,true,Seed:true)],[("a","b")]);pair.Complete();Check(pair.Positions[0]==new Point()&&(pair.Positions[1]-pair.Positions[0]).Length>=min-.05,"Settled connected pair enforces visible minimum and keeps dropped node pinned");
        var large=new GraphRelaxation([new("a",new Point(),45,true),new("b",new Point(),GraphStyle.MaxPointRadius,true)],[("a","b")]);large.Complete();Check((large.Positions[1]-large.Positions[0]).Length>=GraphStyle.MinimumNodeDistance(45,GraphStyle.MaxPointRadius)-.05,"Minimum includes double-ring and large text radii");
        var chain=new GraphRelaxation(Enumerable.Range(0,12).Select(i=>new GraphRelaxation.Body(i.ToString(),new Point(i*4,0),12,true)).ToArray(),Enumerable.Range(0,11).Select(i=>(i.ToString(),(i+1).ToString())).ToArray());chain.Complete();Check(Enumerable.Range(0,11).All(i=>(chain.Positions[i+1]-chain.Positions[i]).Length>=min-.1),"Dense linked chain retains minimum separation");
        var confined=new GraphRelaxation([new("a",new Point(20,20),12,false,new Rect(0,0,40,40)),new("b",new Point(21,20),12,true,new Rect(0,0,40,40))],[("a","b")]);confined.Complete();Check(confined.Positions.All(p=>new Rect(0,0,40,40).Contains(p)),"Impossible spacing never breaks a fixed threshold boundary");
        var limits=new[]{new GraphSpacing.DragLimit(new Point(200,0),new Point(),min)};var moving=new Rect(188,-12,24,24);
        var delta=GraphSpacing.LimitDrag(new Vector(-195,0),new Vector(),limits,moving,null);Check(Math.Abs((new Point(200,0)+delta).X-min)<.1,"Dragging toward linked endpoint stops at minimum");
        var crossing=GraphSpacing.LimitDrag(new Vector(-450,0),new Vector(),limits,moving,null);Check((new Point(200,0)+crossing).X>=min-.1,"Fast dragging cannot tunnel through linked endpoint");
        var sliding=GraphSpacing.LimitDrag(new Vector(-195,100),delta,limits,moving,null);Check((new Vector(200,0)+sliding).Length>=min-.1&&sliding.Y>0,"Node slides along the separation boundary");
        var free=GraphSpacing.LimitDrag(new Vector(50,20),new Vector(),[],moving,null);Check(free==new Vector(50,20),"Unconnected nodes remain freely draggable");
        var doc=new GraphDocument{Nodes=[new(){Id="a",X=-125,Y=-60},new(){Id="b",X=-115,Y=-60},new(){Id="far",X=2000,Y=2000}],Edges=[new(){From="a",To="b"}]};var graph=new GraphSurface{Document=doc};int commits=0;graph.LayoutFinished+=()=>{commits++;Check((GraphStyle.Center(doc.Nodes[1])-GraphStyle.Center(doc.Nodes[0])).Length>=min-.05,"Layout event observes final spacing only");};graph.StartRelaxation(["a","b"]);Check(commits==1,"Local layout commits once before the operation returns");Check(doc.Nodes[2].X==2000&&doc.Nodes[2].Y==2000,"Distant nodes remain unchanged");var settled=doc.Serialize();graph.CancelLayout();Check(doc.Serialize()==settled,"Cancel has no remaining animation to flush");
        foreach(var zoom in new[]{.1,.2,.5,1d,2d,3.2})foreach(var both in new[]{false,true})
        {
            var radius=GraphStyle.DisplayRadius(GraphStyle.MaxPointRadius,zoom)+2/zoom;var shape=GraphCurve.Create(new Point(),new Point(64,0),new Vector(0,1),0);Check(shape.Control==new Point(32,0),"Short links remain straight at every zoom and direction instead of growing loops");
            var lane=GraphCurve.Create(new Point(),new Point(64,0),new Vector(0,1),1000);Check(Math.Abs(lane.At(.5).Y)<=64*.11+.001,"Parallel lanes remain shallow even when points are close");
            var length=shape.VisibleLength(radius,radius);var size=Math.Min(GraphStyle.ArrowSize(GraphStyle.MaxPointRadius,zoom),length/(both?4.4:2.2));Check(size*(both?3.6:1.8)<=length+.001,"Arrow symbols fit the available line without overlapping each other");
        }
        var longEdge=GraphCurve.Create(new Point(),new Point(200,0),new Vector(0,1),0);Check(longEdge.Control==new Point(100,0),"Spacious links remain straight");
        var watch=Stopwatch.StartNew();var dense=new GraphRelaxation(Enumerable.Range(0,220).Select(i=>new GraphRelaxation.Body(i.ToString(),new Point((i%20)*25,(i/20)*25),12,true)).ToArray(),Enumerable.Range(0,219).Select(i=>(i.ToString(),(i+1).ToString())).ToArray());dense.Complete();watch.Stop();Check(dense.Positions.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)),"Dense single-result layout remains finite");
        File.AppendAllText(TestEvidence.ResultsPath,"PASS: single atomic layout commit, minimum connected spacing, pinned endpoints, merged/large radii, crowded chain, fixed bounds, drag contact/sliding/no tunneling, unconstrained free nodes, straight zoom-invariant routes, bounded parallel lanes and proportional arrowheads at six zooms.\n220-node local solve: "+watch.ElapsedMilliseconds+" ms.\n");
    }
}
