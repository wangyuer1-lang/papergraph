using System.Windows;
namespace Papergraph;

public static class GraphBoard
{
    public static Rect Bounds(Region region)=>new(region.X,region.Y,region.Width,region.Height);
    public static List<Proposition> Members(GraphDocument doc,Region region)=>doc.Nodes.Where(n=>n.Parent==region.Parent&&Bounds(region).Contains(GraphGroups.Center(doc,n))).ToList();
    public static List<Region> InnerRegions(GraphDocument doc,Region region)=>doc.Regions.Where(r=>r.Id!=region.Id&&r.Parent==region.Parent&&Bounds(region).Contains(Bounds(r))&&r.Width*r.Height<region.Width*region.Height-.01).ToList();
    public static Point Clamp(Point point,Rect bounds,double inset=0)
    {
        double x=Math.Min(inset,bounds.Width/2),y=Math.Min(inset,bounds.Height/2);
        return new Point(Math.Clamp(point.X,bounds.Left+x,bounds.Right-x),Math.Clamp(point.Y,bounds.Top+y,bounds.Bottom-y));
    }
    public static Vector ClampDelta(Rect moving,Rect parent,Vector delta)
    {
        var x=moving.Width<=parent.Width?Math.Clamp(delta.X,parent.Left-moving.Left,parent.Right-moving.Right):0;
        var y=moving.Height<=parent.Height?Math.Clamp(delta.Y,parent.Top-moving.Top,parent.Bottom-moving.Bottom):0;return new Vector(x,y);
    }
}

public sealed class RegionMove
{
    readonly Region region;readonly Rect original;
    readonly (Proposition Node,double X,double Y)[] nodes;
    readonly (Region Region,double X,double Y)[] regions;
    public RegionMove(GraphDocument doc,Region region,bool withContents)
    {
        this.region=region;original=GraphBoard.Bounds(region);
        var members=withContents?doc.Descendants(GraphBoard.Members(doc,region).Select(n=>n.Id)):[];
        nodes=doc.Nodes.Where(n=>members.Contains(n.Id)).Select(n=>(n,n.X,n.Y)).ToArray();
        var inner=withContents?GraphBoard.InnerRegions(doc,region).Select(r=>r.Id).ToHashSet():[];
        regions=doc.Regions.Where(r=>inner.Contains(r.Id)||r.Parent!=null&&members.Contains(r.Parent)).Select(r=>(r,r.X,r.Y)).ToArray();
    }
    public void Apply(Vector delta,Rect? parent=null)
    {
        if(parent is Rect bounds)delta=GraphBoard.ClampDelta(original,bounds,delta);
        region.X=original.X+delta.X;region.Y=original.Y+delta.Y;
        foreach(var item in nodes){item.Node.X=item.X+delta.X;item.Node.Y=item.Y+delta.Y;}
        foreach(var item in regions){item.Region.X=item.X+delta.X;item.Region.Y=item.Y+delta.Y;}
    }
}

// Explicitly selected objects move once, even when several selected frames overlap.
public sealed class SelectionMove
{
    readonly (Proposition Node,double X,double Y)[] nodes;
    readonly (Region Region,double X,double Y)[] regions;
    public Rect Bounds {get;}
    public IReadOnlyCollection<string> NodeIds {get;}
    public int Count=>nodes.Length+regions.Length;
    public SelectionMove(GraphDocument doc,IEnumerable<string> nodeIds,IEnumerable<string> regionIds)
    {
        var selectedNodes=doc.Descendants(nodeIds);var selectedRegions=regionIds.ToHashSet();
        foreach(var r in doc.Regions.Where(r=>r.Parent!=null&&selectedNodes.Contains(r.Parent)))selectedRegions.Add(r.Id);
        nodes=doc.Nodes.Where(n=>selectedNodes.Contains(n.Id)).Select(n=>(n,n.X,n.Y)).ToArray();regions=doc.Regions.Where(r=>selectedRegions.Contains(r.Id)).Select(r=>(r,r.X,r.Y)).ToArray();NodeIds=nodes.Select(n=>n.Node.Id).ToArray();
        var bounds=Rect.Empty;foreach(var item in nodes)bounds.Union(item.Node.Kind=="circle"?GraphGroups.Bounds(doc,item.Node):GraphStyle.Bounds(item.Node));foreach(var item in regions)bounds.Union(GraphBoard.Bounds(item.Region));Bounds=bounds;
    }
    public void Apply(Vector delta)
    {
        foreach(var item in nodes){item.Node.X=item.X+delta.X;item.Node.Y=item.Y+delta.Y;}
        foreach(var item in regions){item.Region.X=item.X+delta.X;item.Region.Y=item.Y+delta.Y;}
    }
}
