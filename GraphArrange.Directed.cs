using System.Windows;

namespace Papergraph;

public static partial class GraphArrange
{
    // Compare readable directed layouts at their actual container size. No force simulation,
    // fixed-radius compaction, or scale-to-fit step is allowed to squeeze the contents.
    static IEnumerable<Dictionary<string,Point>> DirectedCandidates(Item[] items,List<Link> links,Rect area,Rect? ring,CancellationToken cancellation)
    {
        var ordinal=items.Select((item,i)=>(item.Node.Id,i)).ToDictionary(p=>p.Id,p=>p.i);
        var primary=links.Where(e=>!e.Side).DistinctBy(e=>(e.From,e.To)).ToArray();
        var next=items.ToDictionary(i=>i.Node.Id,_=>new List<string>());
        foreach(var e in primary)next[e.From].Add(e.To);
        int serial=0,components=0;
        var stack=new Stack<string>();var onStack=new HashSet<string>();
        var indices=new Dictionary<string,int>();var low=new Dictionary<string,int>();var component=new Dictionary<string,int>();
        void Visit(string id)
        {
            cancellation.ThrowIfCancellationRequested();
            indices[id]=low[id]=serial++;stack.Push(id);onStack.Add(id);
            foreach(var child in next[id].OrderBy(x=>ordinal[x]))
            {
                if(!indices.ContainsKey(child)){Visit(child);low[id]=Math.Min(low[id],low[child]);}
                else if(onStack.Contains(child))low[id]=Math.Min(low[id],indices[child]);
            }
            if(low[id]!=indices[id])return;
            string member;do{member=stack.Pop();onStack.Remove(member);component[member]=components;}while(member!=id);
            components++;
        }
        foreach(var item in items)if(!indices.ContainsKey(item.Node.Id))Visit(item.Node.Id);
        var arcs=primary.Select(e=>(A:component[e.From],B:component[e.To])).Where(e=>e.A!=e.B).Distinct().ToArray();
        var indegree=new int[components];var rank=new int[components];
        foreach(var e in arcs)indegree[e.B]++;
        var componentOrder=Enumerable.Range(0,components).OrderBy(c=>items.Where(i=>component[i.Node.Id]==c).Min(i=>ordinal[i.Node.Id])).ToArray();
        var remaining=componentOrder.ToHashSet();var order=new List<string>();int? last=null;
        while(remaining.Count>0)
        {
            cancellation.ThrowIfCancellationRequested();
            var chosen=componentOrder.Where(c=>remaining.Contains(c)&&indegree[c]==0)
                .OrderBy(c=>last!=null&&arcs.Any(e=>e.A==last&&e.B==c)?0:1).First();
            remaining.Remove(chosen);
            // A cycle is kept contiguous; traverse its arrows instead of random ID order.
            var members=items.Where(i=>component[i.Node.Id]==chosen).Select(i=>i.Node.Id).ToHashSet();
            string? prior=null;
            while(members.Count>0)
            {
                var id=members.OrderBy(id=>prior!=null&&next[prior].Contains(id)?0:1).ThenBy(id=>ordinal[id]).First();
                order.Add(id);members.Remove(id);prior=id;
            }
            foreach(var e in arcs.Where(e=>e.A==chosen)){rank[e.B]=Math.Max(rank[e.B],rank[chosen]+1);indegree[e.B]--;}
            last=chosen;
        }
        var layers=order.GroupBy(id=>rank[component[id]]).OrderBy(g=>g.Key).Select(g=>g.ToList()).ToArray();
        for(int sweep=0;sweep<6;sweep++)
        {
            var positions=layers.SelectMany(l=>l.Select((id,i)=>(id,position:(i+.5)/l.Count))).ToDictionary(p=>p.id,p=>p.position);
            foreach(var layer in sweep%2==0?layers:layers.Reverse())
            {
                double Barycenter(string id)
                {
                    var adjacent=primary.Where(e=>sweep%2==0?e.To==id:e.From==id)
                        .Select(e=>sweep%2==0?e.From:e.To).Where(n=>rank[component[n]]!=rank[component[id]]).ToArray();
                    return adjacent.Length==0?positions[id]:adjacent.Average(n=>positions[n]);
                }
                var sorted=layer.OrderBy(Barycenter).ThenBy(id=>positions[id]).ToArray();layer.Clear();layer.AddRange(sorted);
                for(int i=0;i<layer.Count;i++)positions[layer[i]]=(i+.5)/layer.Count;
            }
        }

        var candidates=new List<(Dictionary<string,Point> Positions,double Score,int Sequence)>();
        void Add(Dictionary<string,Point>? positions,double preference)
        {
            if(positions==null)return;
            if(ring is Rect envelope)positions=FillRing(items,positions,envelope);
            if(positions==null)return;
            var score=LayoutScore(items,links,positions,area)+preference;
            candidates.Add((positions,score,candidates.Count));
        }
        bool preferredVertical=area.Height>area.Width*1.15;
        foreach(var vertical in new[]{preferredVertical,!preferredVertical})
        {
            // Proper layers are preferred for branches; a long chain can wrap as reading rows.
            Add(Distribute(items,layers,area,vertical,ring!=null),vertical==preferredVertical?0:3);
            int limit=Math.Min(items.Length,128);
            for(int count=1;count<=limit;count++)
            {
                cancellation.ThrowIfCancellationRequested();
                var rows=order.Chunk(count).Select((row,i)=>i%2==0?row.ToList():row.Reverse().ToList()).ToArray();
                Add(Distribute(items,rows,area,!vertical,ring!=null),5+(vertical==preferredVertical?0:3));
            }
        }
        // Explicit cycles also get a perimeter candidate with no crossing closing edge.
        if(components==1&&items.Length>2)
        {
            var center=Center(area);var r=Math.Min(area.Width,area.Height)/2-items.Max(i=>i.Box.Width/2)-32;
            if(r>0)Add(order.Select((id,i)=>(id,p:center+new Vector(Math.Cos(-Math.PI/2+i*2*Math.PI/order.Count)*r,Math.Sin(-Math.PI/2+i*2*Math.PI/order.Count)*r))).ToDictionary(p=>p.id,p=>p.p),0);
        }
        return candidates.OrderBy(c=>c.Score).ThenBy(c=>c.Sequence).Select(c=>c.Positions);
    }

    // Along the reading direction, layers/rows consume the available span. Across it,
    // each row has its own widths so small points don't reserve an entire ring-sized cell.
    static Dictionary<string,Point>? Distribute(Item[] items,List<string>[] layers,Rect area,bool vertical,bool ring)
    {
        var byId=items.ToDictionary(i=>i.Node.Id);
        double Width(string id)=>ring?byId[id].Box.Width+20:byId[id].Width;
        double Height(string id)=>ring?byId[id].Box.Height+28:byId[id].Height;
        double Along(string id)=>vertical?Height(id):Width(id);
        double Across(string id)=>vertical?Width(id):Height(id);
        double depth=vertical?area.Height:area.Width,breadth=vertical?area.Width:area.Height;
        const double gap=88;
        var sizes=layers.Select(l=>l.Max(Along)).ToArray();
        var requiredDepth=sizes.Sum()+gap*(layers.Length-1);
        var requiredBreadth=layers.Max(l=>l.Sum(Across)+gap*(l.Count-1));
        // A ring is normalized to its existing circumference afterwards. Validate actual
        // spacing there instead of rejecting a rectangular scaffold prematurely.
        if(!ring&&(requiredDepth>depth||requiredBreadth>breadth))return null;
        if(ring){depth=requiredDepth;breadth=requiredBreadth;}
        double alongGap=layers.Length>1?(depth-sizes.Sum())/(layers.Length-1):0;
        var center=Center(area);var result=new Dictionary<string,Point>();
        double cursor=layers.Length==1?-sizes[0]/2:-depth/2;
        for(int i=0;i<layers.Length;i++)
        {
            var layer=layers[i];double acrossGap=layer.Count>1?(breadth-layer.Sum(Across))/(layer.Count-1):0;
            double side=layer.Count==1?-Across(layer[0])/2:-breadth/2;
            foreach(var id in layer)
            {
                double a=cursor+sizes[i]/2,b=side+Across(id)/2;
                result[id]=center+(vertical?new Vector(b,a):new Vector(a,b));
                side+=Across(id)+acrossGap;
            }
            cursor+=sizes[i]+alongGap;
        }
        return result;
    }

    // Match GraphGroups' derived circle exactly. This preserves its visible center and
    // diameter, including unequal point sizes, instead of shrinking on every click.
    static Dictionary<string,Point>? FillRing(Item[] items,Dictionary<string,Point> positions,Rect envelope)
    {
        var origin=Center(envelope);double target=envelope.Width/2;
        var vectors=positions.ToDictionary(p=>p.Key,p=>p.Value-origin);
        (Point Center,double Radius) Measure(double scale)
        {
            var box=Rect.Empty;
            foreach(var item in items)
            {
                var p=origin+vectors[item.Node.Id]*scale;double r=item.Box.Width/2;
                box.Union(new Rect(p.X-r,p.Y-r,2*r,2*r));
            }
            var center=Center(box);
            return (center,items.Max(item=>(origin+vectors[item.Node.Id]*scale-center).Length+item.Box.Width/2)+28);
        }
        if(Measure(0).Radius>=target)return null;
        double low=0,high=1;
        while(Measure(high).Radius<target&&high<1e6)high*=2;
        for(int i=0;i<64;i++){double mid=(low+high)/2;if(Measure(mid).Radius<target)low=mid;else high=mid;}
        double factor=(low+high)/2;var shift=origin-Measure(factor).Center;
        return vectors.ToDictionary(p=>p.Key,p=>origin+p.Value*factor+shift);
    }

    static double LayoutScore(Item[] items,List<Link> links,Dictionary<string,Point> positions,Rect area)
    {
        var radii=items.ToDictionary(i=>i.Node.Id,i=>i.Box.Width/2);
        var unique=links.DistinctBy(e=>string.CompareOrdinal(e.From,e.To)<0?(e.From,e.To):(e.To,e.From)).ToArray();
        double score=0;
        for(int i=0;i<unique.Length;i++)
        {
            var edge=unique[i];var a=positions[edge.From];var b=positions[edge.To];
            foreach(var item in items)
                if(item.Node.Id!=edge.From&&item.Node.Id!=edge.To&&GraphStyle.Distance(positions[item.Node.Id],a,b)<radii[item.Node.Id]+18)score+=100000;
            for(int j=i+1;j<unique.Length;j++)
            {
                var other=unique[j];if(edge.From==other.From||edge.From==other.To||edge.To==other.From||edge.To==other.To)continue;
                if(Crosses(a,b,positions[other.From],positions[other.To]))score+=100000;
            }
            score+=(b-a).Length/Math.Max(1,Math.Sqrt(area.Width*area.Height));
            // Prefer comfortable shafts, not just the hard minimum at which an arrow
            // barely fits. This matters when point glyphs stay readable while zoomed out.
            var shaft=(b-a).Length-radii[edge.From]-radii[edge.To];
            score+=40*Math.Max(0,(140-shaft)/140);
        }
        foreach(var node in items)
        {
            var before=links.Where(e=>!e.Side&&e.To==node.Node.Id).ToArray();var after=links.Where(e=>!e.Side&&e.From==node.Node.Id).ToArray();
            if(before.Length!=1||after.Length!=1)continue;
            var incoming=positions[node.Node.Id]-positions[before[0].From];var outgoing=positions[after[0].To]-positions[node.Node.Id];
            if(incoming.Length>0&&outgoing.Length>0)score+=12*(1-Vector.Multiply(incoming,outgoing)/(incoming.Length*outgoing.Length));
        }
        var used=Rect.Empty;
        foreach(var item in items)
        {
            var p=positions[item.Node.Id];var box=new Rect(p.X-item.Width/2,p.Y-item.Height/2,item.Width,item.Height);used.Union(box);
        }
        double xWeight=Math.Min(1,2*area.Width/area.Height),yWeight=Math.Min(1,2*area.Height/area.Width);
        score+=50*xWeight*(1-Math.Min(1,used.Width/area.Width))+50*yWeight*(1-Math.Min(1,used.Height/area.Height));
        // Very uneven link lengths usually indicate a branch was flattened into a zigzag.
        var lengths=unique.Select(e=>(positions[e.From]-positions[e.To]).Length).ToArray();
        if(lengths.Length>1&&lengths.Min()>0)score+=4*Math.Min(20,lengths.Max()/lengths.Min()-1);
        return score;
    }

    static bool Crosses(Point a,Point b,Point c,Point d)
    {
        double Cross(Vector x,Vector y)=>x.X*y.Y-x.Y*y.X;
        return Cross(b-a,c-a)*Cross(b-a,d-a)<-.01&&Cross(d-c,a-c)*Cross(d-c,b-c)<-.01;
    }
}
