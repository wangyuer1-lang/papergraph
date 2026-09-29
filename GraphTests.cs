using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Papergraph;
public static class GraphTests
{
    public static void Run()
    {
        void Assert(bool success,string message){if(!success)throw new Exception(message);}
        Assert(GraphStyle.Radius(0)==12&&GraphStyle.Radius(-1)==12,"Small empty point retains its original radius");
        Assert(GraphStyle.Radius(100)-GraphStyle.Radius(20)>6,"Short claims and paragraphs have visibly different diameters");
        Assert(GraphStyle.Radius(400)-GraphStyle.Radius(100)>7,"Medium and long paragraphs have visibly different diameters");
        Assert(Math.Pow(GraphStyle.Radius(100)/GraphStyle.Radius(20),2)>2,"A 100-character claim has over twice the area of a 20-character claim");
        Assert(GraphStyle.Radius(1600)>GraphStyle.Radius(800)&&GraphStyle.Radius(1600)-GraphStyle.Radius(800)<2,"Very long text grows slowly near the cap");
        double previous=GraphStyle.Radius(0);foreach(var count in new[]{1,10,20,50,100,200,400,800,1600,5000,100000,int.MaxValue}){var radius=GraphStyle.Radius(count);Assert(radius>previous&&radius<=GraphStyle.MaxPointRadius,"Size grows monotonically and never exceeds its cap");previous=radius;}
        Assert(GraphStyle.Radius(new Proposition{Kind="circle"})>GraphStyle.MaxPointRadius,"Empty merged point exceeds largest ordinary point");Assert(GraphStyle.ArrowSize(GraphStyle.MaxPointRadius,1)>GraphStyle.ArrowSize(12,1),"Arrows scale with endpoint circles");
        foreach(var zoom in new[]{.10,.20,.25,.35,.65,1,2}){Assert(GraphStyle.DisplayRadius(12,zoom)*zoom>=6,"Overview node stays visible");Assert(GraphStyle.DisplayRadius(GraphStyle.MaxPointRadius,zoom)>GraphStyle.DisplayRadius(12,zoom),"Text-size hierarchy survives overview");Assert(GraphStyle.DisplayRadius(GraphStyle.Radius(400),zoom)*zoom-GraphStyle.DisplayRadius(GraphStyle.Radius(20),zoom)*zoom>2.8,"Overview retains a noticeable short-versus-long size difference");Assert(GraphStyle.DisplayRadius(GraphStyle.Radius(new Proposition{Kind="circle"}),zoom)>GraphStyle.DisplayRadius(GraphStyle.MaxPointRadius,zoom),"Double-ring body stays larger at every zoom");Assert(GraphStyle.ArrowSize(12,zoom)*zoom>=7.49,"Readable overview arrowhead");Assert(Math.Abs(GraphStyle.EdgeWidth(zoom)*zoom-2.1)<.001,"Constant screen-space edge thickness");}
        Assert(Math.Abs(GraphStyle.DisplayRadius(20,.999999)*.999999-20)<.0001,"Overview scaling joins normal zoom continuously");
        var crowded=new GraphDocument{Nodes=[new(){Id="a",X=-125,Y=-60},new(){Id="b",X=-45,Y=-60}]};var picking=new GraphSurface{Document=crowded,Selected=["a"]};picking.SetView(.2,new Point(20,20));Assert(picking.HitNode(new Point(70,0))?.Id=="b","Nearest visible node wins over selected neighbor padding");Assert(picking.HitNode(new Point(135,0))?.Id=="b","Small node has forgiving hit target");var priorData=crowded.Serialize();picking.Measure(new Size(600,400));picking.Arrange(new Rect(0,0,600,400));picking.SetView(.2,new Point(40,70));picking.ToggleDetail(smooth:false);Assert(picking.Zoom>=1,"Local detail becomes operable");picking.ToggleDetail(smooth:false);Assert(picking.Zoom==.2&&picking.Offset==new Point(40,70)&&crowded.Serialize()==priorData,"Local detail restores exact overview without mutation");
        var coloring=new GraphDocument{Nodes=[new(){Id="left",X=-65,Y=20},new(){Id="right",X=195,Y=20},new(){Id="bottom",X=195,Y=120}],Edges=[new(){From="left",To="right"}]};var visual=new GraphSurface{Document=coloring};visual.Measure(new Size(420,260));visual.Arrange(new Rect(0,0,420,260));visual.SetView(1,new Point(0,0));
        byte[] Pixels(){visual.UpdateLayout();var bitmap=new RenderTargetBitmap(420,260,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var data=new byte[420*260*4];bitmap.CopyPixels(data,420*4,0);return data;}
        foreach(var dark in new[]{false,true}){visual.ApplyTheme(dark);visual.Selected.Clear();visual.RefreshSelection();var before=Pixels();visual.Selected=["left"];visual.RefreshSelection();var after=Pixels();foreach(var sample in new[]{(320,80),(320,180),(180,80)}){int i=(sample.Item2*420+sample.Item1)*4;Assert(before.AsSpan(i,4).SequenceEqual(after.AsSpan(i,4)),"Selecting a node preserves every other node and edge color");}}
        var n=new Proposition{X=47,Y=83};var center=GraphStyle.Center(n);n.Title=new string('字',100000);var bounds=GraphStyle.Bounds(n);Assert(new Point(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2)==center,"Typing does not move node center");
        foreach(var dark in new[]{false,true})foreach(var color in new[]{"#000000","#FFFFFF","#719AC7","#73A899","#C1B66E","#C58F9D"})Assert(GraphStyle.Contrast(GraphStyle.NodeColor(color,dark),(Color)ColorConverter.ConvertFromString(dark?"#191C22":"#F7F8FA"))>=3,"Node contrast in both themes");
        var hashes=new HashSet<string>();foreach(var label in GraphStyle.Relations.Append("自定义"))
        {
            var glyph=new EdgeGlyph{Label=label};glyph.Measure(new Size(35,23));glyph.Arrange(new Rect(0,0,35,23));glyph.UpdateLayout();var bitmap=new RenderTargetBitmap(35,23,96,96,PixelFormats.Pbgra32);bitmap.Render(glyph);var bytes=new byte[35*23*4];bitmap.CopyPixels(bytes,35*4,0);Assert(hashes.Add(Convert.ToHexString(SHA256.HashData(bytes))),"Arrow glyphs are visually distinct");
        }
        Assert(GraphStyle.Distance(new Point(50,4),new Point(0,0),new Point(100,0))==4,"Edge hit distance");
        var changes=GraphDocument.Demo();changes.MaterializeRegions();var fixedRegion=changes.Regions.Single();fixedRegion.Note="阈的笔记";var beforeBox=(fixedRegion.X,fixedRegion.Y,fixedRegion.Width,fixedRegion.Height);changes.Node("b")!.X+=400;Assert(beforeBox==(fixedRegion.X,fixedRegion.Y,fixedRegion.Width,fixedRegion.Height),"Absolute threshold independent of points");changes.DeleteNodes(["b","c"]);changes.Validate();Assert(changes.Regions.Any(r=>r.Id==fixedRegion.Id),"Empty fixed threshold survives node deletion");
        changes=GraphDocument.Demo();var combined=changes.Collapse(["a","b"],null);combined.Title="合并命题";combined.Note="保留这份笔记";var direct=new Relation{From=combined.Id,To="d",Direction="both"};changes.Edges.Add(direct);changes.Validate();changes.Dissolve(combined.Id);changes.Validate();Assert(changes.Node(combined.Id)?.Kind=="point"&&changes.Node(combined.Id)?.Note=="保留这份笔记"&&changes.Edges.Contains(direct),"Unmerge retains aggregate note and direct endpoints");Assert(changes.Node("a")?.Parent==null&&changes.Node("b")?.Parent==null,"Unmerge restores members");
        var serialized=GraphDocument.Parse(changes.Serialize());Assert(serialized.Edges.First(e=>e.Id==direct.Id).Direction=="both","Bidirectional relation survives persistence");Assert(serialized.Markdown().Contains("↔"),"Bidirectional Markdown export");
        var layout=GraphLayout.Arrange(Enumerable.Range(0,12).Select(i=>(Id:i.ToString(),Center:new Point(0,0),Radius:20d)).ToArray(),Enumerable.Range(0,11).Select(i=>(From:i.ToString(),To:(i+1).ToString())).ToArray());
        Assert(layout.Count==12&&layout.Values.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)),"Layout remains finite from coincident nodes");Assert(layout.Values.SelectMany((p,i)=>layout.Values.Skip(i+1).Select(q=>(p-q).Length)).Min()>40,"Layout separates overlapping nodes");
        var doc=new GraphDocument();for(int i=0;i<200;i++)doc.Nodes.Add(new Proposition{Id="n"+i,Caption="P"+i,Title="命题 "+i,X=(i%20)*90,Y=(i/20)*90});for(int i=0;i<399;i++)doc.Edges.Add(new Relation{From="n"+(i%200),To="n"+((i+1)%200),Label=GraphStyle.Relations[i%6]});
        var graph=new GraphSurface{Document=doc,ShowCaptions=true};graph.Measure(new Size(1200,800));graph.Arrange(new Rect(0,0,1200,800));graph.Fit(false);var watch=Stopwatch.StartNew();for(int i=0;i<10;i++){graph.ApplyTheme(i%2==0);graph.UpdateLayout();var bitmap=new RenderTargetBitmap(1200,800,96,96,PixelFormats.Pbgra32);bitmap.Render(graph);}watch.Stop();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: distinct size curve/cap, merged point hierarchy, proportional arrows, stable center, light/dark contrast, seven arrow symbols, edge hit testing, fixed threshold independence, lossless unmerge with notes/direct edges, bidirectional persistence/export, overlapping-node layout.\n200 nodes / 399 edges, 10 offscreen theme renders: "+watch.ElapsedMilliseconds+" ms (not an interactive FPS measurement).\n");
    }
}


