using System.Windows;
namespace Papergraph;

public sealed partial class GraphSurface
{
    internal string? ConnectionEndpointTarget(Point world,string? source)
    {
        // A point under the pointer always wins over its enclosing frame.
        if(ConnectionTarget(world,source) is Proposition node)return node.Id;
        var frame=HitRegion(world);
        if(frame!=null&&frame.Id!=source)return frame.Id;
        return regions.FirstOrDefault(r=>r.Id!=source&&(world-RegionPort(r)).Length*Zoom<=11)?.Id;
    }
    internal Point RegionPort(Region frame)
    {
        var box=RegionBounds(frame);return new Point(box.Right+13/Zoom,box.Top+Math.Min(box.Height/2,38/Zoom));
    }
    Point EndpointCenter(string id)
    {
        if(index.TryGetValue(id,out var node))return ObjectCenter(node);
        var box=RegionBounds(document.Frame(id)!);return new Point(box.X+box.Width/2,box.Y+box.Height/2);
    }
    internal Point EndpointAnchor(string id,Point toward)
    {
        if(index.TryGetValue(id,out var node))return Anchor(node,toward);
        var box=RegionBounds(document.Frame(id)!);var center=EndpointCenter(id);var delta=toward-center;
        if(delta.Length<.001)delta=new Vector(1,0);
        var scale=Math.Min(Math.Abs(delta.X)<.00001?double.PositiveInfinity:box.Width/2/Math.Abs(delta.X),Math.Abs(delta.Y)<.00001?double.PositiveInfinity:box.Height/2/Math.Abs(delta.Y));
        var boundary=center+delta*scale;delta.Normalize();
        return boundary+delta*(box.Contains(toward)?-2/Zoom:2/Zoom);
    }
    void SelectEndpoint(string id)
    {
        ClearAllSelection();if(document.Frame(id)!=null)SelectedRegion=id;else Selected=[id];
    }
}
