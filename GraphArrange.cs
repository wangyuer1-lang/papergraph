using System.Windows;

namespace Papergraph;

// Explicit, bounded layout. Frames are fixed containers; rings move as whole units.
// Text and relation semantics are never inferred or changed by this operation.
public static partial class GraphArrange
{
    public sealed record Result(Dictionary<string,Point> Positions,int Arranged,int Crowded);
    sealed record Item(Proposition Node,Rect Box,double Width,double Height);
    sealed record Link(string From,string To,bool Side);

    public static Result Compute(GraphDocument document,string? scope,string? boardRegion,
        IEnumerable<string> selectedNodes,IEnumerable<string> selectedRegions,CancellationToken cancellation=default)
    {
        var doc=GraphDocument.Parse(document.Serialize());
        var original=doc.Nodes.ToDictionary(n=>n.Id,n=>new Point(n.X,n.Y));
        var membership=Membership(doc);
        var selectedFrames=selectedRegions.Select(id=>doc.Regions.FirstOrDefault(r=>r.Id==id)).OfType<Region>().ToArray();
        var selectedRings=GraphGroups.SelectionRoots(doc,selectedNodes).Where(n=>n.Kind=="circle").ToArray();
        HashSet<string> allowed;
        if(selectedFrames.Length+selectedRings.Length>0)
            allowed=doc.Descendants(selectedFrames.SelectMany(r=>GraphBoard.Members(doc,r)).Select(n=>n.Id).Concat(selectedRings.Select(n=>n.Id)));
        else if(boardRegion!=null&&doc.Regions.FirstOrDefault(r=>r.Id==boardRegion) is Region board)
            allowed=doc.Descendants(GraphBoard.Members(doc,board).Select(n=>n.Id));
        else
        {
            allowed=doc.Descendants(doc.Visible(scope).Select(n=>n.Id));
            // In an overview, leave objects outside all frames where the author put them.
            if(scope==null&&doc.Regions.Any(r=>r.Parent==null))allowed.RemoveWhere(id=>membership[id].Count==0);
        }
        var selectedRingIds=selectedRings.Select(n=>n.Id).ToHashSet();
        // A selected ring's own position stays fixed; only its contents are arranged.
        allowed.ExceptWith(selectedRingIds);
        var ringBounds=doc.Nodes.Where(n=>n.Kind=="circle").ToDictionary(n=>n.Id,n=>GraphGroups.Bounds(doc,n));
        int arranged=0,crowded=0;
        int Depth(Proposition n){int d=0;while(n.Parent!=null){d++;n=doc.Node(n.Parent)!;}return d;}
        var parents=doc.Nodes.Where(n=>allowed.Contains(n.Id)).Select(n=>n.Parent).Distinct()
            .OrderByDescending(id=>id==null?-1:Depth(doc.Node(id)!)).ToArray();
        foreach(var parent in parents)
        {
            cancellation.ThrowIfCancellationRequested();
            // Exact memberships keep intersections and nested frame partitions separate.
            var partitions=doc.Visible(parent).Where(n=>allowed.Contains(n.Id))
                .GroupBy(n=>string.Join("|",membership[n.Id].Order(StringComparer.Ordinal))).ToArray();
            foreach(var partition in partitions)
            {
                cancellation.ThrowIfCancellationRequested();
                var roots=partition.ToArray();if(roots.Length<2)continue;
                var moving=doc.Descendants(roots.Select(n=>n.Id));
                // Partially selected nested structures are not pulled along accidentally.
                if(moving.Any(id=>!allowed.Contains(id)))continue;
                var frameIds=membership[roots[0].Id];
                Rect? bounds=null;
                foreach(var frame in doc.Regions.Where(r=>frameIds.Contains(r.Id)))
                    bounds=bounds is Rect prior?Rect.Intersect(prior,GraphBoard.Bounds(frame)):GraphBoard.Bounds(frame);
                if(parent!=null&&ringBounds.TryGetValue(parent,out var enclosing))
                    bounds=bounds is Rect prior?Rect.Intersect(prior,enclosing):enclosing;
                var oldBox=Rect.Empty;
                foreach(var n in roots)oldBox.Union(ObjectBounds(doc,n));
                if(bounds==null){var free=oldBox;free.Inflate(120,120);bounds=free;}
                if(bounds.Value.IsEmpty||bounds.Value.Width<50||bounds.Value.Height<50){crowded++;continue;}
                var area=bounds.Value;
                var margin=Math.Max(28,Math.Min(110,Math.Min(area.Width,area.Height)*.06));
                area.Inflate(-margin,-margin);
                var ids=roots.Select(n=>n.Id).ToHashSet();
                var links=new List<Link>();
                foreach(var edge in doc.Edges)
                {
                    var a=doc.Project(edge.From,parent);var b=doc.Project(edge.To,parent);
                    if(a==null||b==null||a==b||!ids.Contains(a)||!ids.Contains(b))continue;
                    if(edge.Direction=="reverse")(a,b)=(b,a);
                    // Arrow direction, not the relation's glyph, determines reading order.
                    var side=edge.Direction=="both";
                    links.Add(new(a,b,side));
                }
                var items=roots.Select(n=>
                {
                    var box=ObjectBounds(doc,n);
                    var titleWidth=n.Caption.Sum(ch=>ch>255?16d:8d)+20;
                    return new Item(n,box,Math.Max(box.Width+16,Math.Min(300,titleWidth)),box.Height+40);
                }).ToArray();
                var backup=doc.Nodes.Where(n=>moving.Contains(n.Id)).ToDictionary(n=>n.Id,n=>new Point(n.X,n.Y));
                var frameBackup=doc.Regions.Where(r=>r.Parent!=null&&moving.Contains(r.Parent)).ToDictionary(r=>r.Id,r=>new Point(r.X,r.Y));
                bool accepted=false;
                var envelope=parent!=null&&ringBounds.TryGetValue(parent,out var ring)?ring:(Rect?)null;
                var candidates=DirectedCandidates(items,links,area,envelope,cancellation);
                foreach(var centers in candidates)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if(centers==null)continue;
                    foreach(var item in items)
                    {
                        var delta=centers[item.Node.Id]-Center(item.Box);
                        Translate(doc,item.Node,delta);
                    }
                    // Keep a ring in its existing envelope, rather than letting its outline drift.
                    if(parent!=null&&ringBounds.TryGetValue(parent,out var oldRing))
                    {
                        var now=GraphGroups.Bounds(doc,doc.Node(parent)!);
                        var delta=Center(oldRing)-Center(now);
                        foreach(var id in moving){doc.Node(id)!.X+=delta.X;doc.Node(id)!.Y+=delta.Y;}
                        foreach(var r in doc.Regions.Where(r=>r.Parent!=null&&moving.Contains(r.Parent))){r.X+=delta.X;r.Y+=delta.Y;}
                    }
                    accepted=Valid(doc,document,membership,moving,frameIds,parent,ringBounds,links);
                    if(accepted)break;
                    Restore();
                }
                if(accepted)arranged++;else{Restore();crowded++;}
                void Restore()
                {
                    foreach(var (id,p) in backup){doc.Node(id)!.X=p.X;doc.Node(id)!.Y=p.Y;}
                    foreach(var (id,p) in frameBackup){var r=doc.Regions.Single(r=>r.Id==id);r.X=p.X;r.Y=p.Y;}
                }
            }
        }
        // Absolute frames never move during tidy, including frames inside rings.
        // Ring units containing such a frame are therefore held in place by validation.
        var changed=doc.Nodes.Where(n=>(new Point(n.X,n.Y)-original[n.Id]).Length>.001)
            .ToDictionary(n=>n.Id,n=>new Point(n.X,n.Y));
        return new(changed,arranged,crowded);
    }

    static Dictionary<string,HashSet<string>> Membership(GraphDocument doc)
    {
        var map=doc.Nodes.ToDictionary(n=>n.Id,_=>new HashSet<string>());
        foreach(var frame in doc.Regions)
            foreach(var id in doc.Descendants(GraphBoard.Members(doc,frame).Select(n=>n.Id)))map[id].Add(frame.Id);
        return map;
    }
    static Rect ObjectBounds(GraphDocument doc,Proposition n)=>n.Kind=="circle"?GraphGroups.Bounds(doc,n):GraphStyle.Bounds(n);
    static Point Center(Rect r)=>new(r.X+r.Width/2,r.Y+r.Height/2);
    static void Translate(GraphDocument doc,Proposition root,Vector delta)
    {
        var ids=doc.Descendants([root.Id]);
        foreach(var n in doc.Nodes.Where(n=>ids.Contains(n.Id))){n.X+=delta.X;n.Y+=delta.Y;}
        foreach(var r in doc.Regions.Where(r=>r.Parent!=null&&ids.Contains(r.Parent))){r.X+=delta.X;r.Y+=delta.Y;}
    }
    static bool Valid(GraphDocument doc,GraphDocument original,Dictionary<string,HashSet<string>> membership,
        HashSet<string> moving,HashSet<string> frames,string? parent,Dictionary<string,Rect> ringBounds,List<Link> links)
    {
        foreach(var r in doc.Regions)
        {
            var old=original.Regions.Single(x=>x.Id==r.Id);
            if(GraphBoard.Bounds(r)!=GraphBoard.Bounds(old))return false;
        }
        var after=Membership(doc);
        if(membership.Any(p=>!p.Value.SetEquals(after[p.Key])))return false;
        foreach(var frame in doc.Regions.Where(r=>frames.Contains(r.Id)))
            foreach(var n in doc.Nodes.Where(n=>moving.Contains(n.Id)))
                if(!GraphBoard.Bounds(frame).Contains(ObjectBounds(doc,n)))return false;
        if(parent!=null&&ringBounds.TryGetValue(parent,out var bounds))
        {
            var current=GraphGroups.Bounds(doc,doc.Node(parent)!);
            // A rearrangement must not turn a roomy ring into a tiny cluster.
            if(Math.Abs(current.Width-bounds.Width)>.02)return false;
            bounds.Inflate(.01,.01);if(!bounds.Contains(current))return false;
        }
        foreach(var link in links)
        {
            var a=doc.Node(link.From)!;var b=doc.Node(link.To)!;
            var ba=ObjectBounds(doc,a);var bb=ObjectBounds(doc,b);
            var minimum=ba.Width/2+bb.Width/2+GraphStyle.MinimumEdgeLength+8;
            if((Center(ba)-Center(bb)).Length<minimum-.1)return false;
        }
        var roots=doc.Nodes.Where(n=>moving.Contains(n.Id)&&n.Parent==parent).ToArray();
        for(int i=0;i<roots.Length;i++)for(int j=i+1;j<roots.Length;j++)
        {
            var a=ObjectBounds(doc,roots[i]);var b=ObjectBounds(doc,roots[j]);
            if((Center(a)-Center(b)).Length<a.Width/2+b.Width/2+12)return false;
        }
        return true;
    }

}
