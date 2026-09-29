using System.Windows;
namespace Papergraph;

// Groups retain explicit membership; their outline is derived from their contents.
public static class GraphGroups
{
    public static Rect Bounds(GraphDocument doc,Proposition group,double zoom=1)
    {
        var children=doc.Nodes.Where(n=>n.Parent==group.Id).Select(n=>n.Kind=="circle"?Bounds(doc,n,zoom):PointBounds(n,zoom)).ToList();
        var frames=doc.Regions.Where(r=>r.Parent==group.Id&&r.IsAbsolute).Select(GraphBoard.Bounds).ToList();
        if(children.Count+frames.Count==0){var p=GraphStyle.Center(group);return new Rect(p.X-70,p.Y-70,140,140);}
        var box=Rect.Empty;foreach(var child in children.Concat(frames))box.Union(child);var center=new Point(box.X+box.Width/2,box.Y+box.Height/2);
        var radius=children.Select(r=>(new Point(r.X+r.Width/2,r.Y+r.Height/2)-center).Length+r.Width/2)
            .Concat(frames.Select(r=>new[]{r.TopLeft,r.TopRight,r.BottomLeft,r.BottomRight}.Max(p=>(p-center).Length))).Max()+Math.Max(28,12/zoom);
        return new Rect(center.X-radius,center.Y-radius,radius*2,radius*2);
    }
    public static Point Center(GraphDocument doc,Proposition node){if(node.Kind!="circle")return GraphStyle.Center(node);var b=Bounds(doc,node);return new Point(b.X+b.Width/2,b.Y+b.Height/2);}
    public static Rect PointBounds(Proposition node,double zoom)
    {
        var p=GraphStyle.Center(node);var radius=GraphStyle.DisplayRadius(GraphStyle.Radius(node),zoom);return new Rect(p.X-radius,p.Y-radius,2*radius,2*radius);
    }
    public static List<Proposition> SelectionRoots(GraphDocument doc,IEnumerable<string> selection)
    {
        var ids=selection.ToHashSet();return doc.Nodes.Where(n=>ids.Contains(n.Id)&&!HasSelectedParent(n)).ToList();
        bool HasSelectedParent(Proposition n){while(n.Parent!=null){if(ids.Contains(n.Parent))return true;n=doc.Node(n.Parent)!;}return false;}
    }
    public static bool CanGroup(GraphDocument doc,IEnumerable<string> selection)
    {
        var roots=SelectionRoots(doc,selection);return roots.Count>=2&&roots.All(n=>n.Parent==roots[0].Parent)&&doc.Connected(roots.Select(n=>n.Id),roots[0].Parent);
    }
    public static bool ExpandLegacy(GraphDocument doc)
    {
        bool changed=false;
        void Expand(Proposition group)
        {
            var children=doc.Visible(group.Id);
            if(!group.Expanded)
            {
                // Old collapsed points could move independently of their hidden contents.
                var shift=children.Count==0?new Vector():GraphStyle.Center(group)-new Point(children.Average(n=>GraphStyle.Center(n).X),children.Average(n=>GraphStyle.Center(n).Y));
                foreach(var child in children){child.X+=shift.X;child.Y+=shift.Y;}
                foreach(var frame in doc.Regions.Where(r=>r.Parent==group.Id)){frame.X+=shift.X;frame.Y+=shift.Y;}
                group.Expanded=true;changed=true;
            }
            foreach(var child in children.Where(n=>n.Kind=="circle"))Expand(child);
        }
        foreach(var group in doc.Visible(null).Where(n=>n.Kind=="circle"))Expand(group);return changed;
    }
}
