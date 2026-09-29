using System.Windows;
using System.Windows.Media;
namespace Papergraph;

// Region colors are a view of spatial membership, never an overwrite of a point's own color.
public static class GraphRegionColors
{
    static readonly string[] palette=["#618FBC","#579A88","#A184BA","#BF8770","#B77891","#A39955","#629EAC","#879658","#777FB1","#B47768","#A36DA1","#589A73"];
    static readonly Dictionary<string,string> combinations=[];
    static uint Hash(string text){uint value=2166136261;foreach(char c in text)value=unchecked((value^c)*16777619);return value;}
    public static string Base(Region region)=>region.Color.Length>0?region.Color:palette[Hash(region.Id)%(uint)palette.Length];
    public static string NewColor(IEnumerable<Region> regions)
    {
        var used=regions.Select(Base).ToHashSet();var choices=palette.Where(c=>!used.Contains(c)).ToArray();if(choices.Length==0){var last=regions.LastOrDefault();choices=palette.Where(c=>last==null||c!=Base(last)).ToArray();}return choices[Random.Shared.Next(choices.Length)];
    }
    public static string Combined(IEnumerable<Region> membership)
    {
        var members=membership.OrderBy(r=>r.Id,StringComparer.Ordinal).ToArray();if(members.Length==1)return Base(members[0]);if(members.Length==0)return palette[0];
        var key=string.Join("|",members.Select(r=>r.Id+":"+Base(r)));if(combinations.TryGetValue(key,out var known))return known;
        var used=members.Select(r=>(Color)ColorConverter.ConvertFromString(Base(r))).ToArray();var offset=(int)(Hash(string.Join("|",members.Select(r=>r.Id)))%(uint)palette.Length);
        var result=Enumerable.Range(0,palette.Length).Select(i=>palette[(i+offset)%palette.Length]).MaxBy(c=>{var color=(Color)ColorConverter.ConvertFromString(c);return used.Min(p=>Math.Pow(p.R-color.R,2)+Math.Pow(p.G-color.G,2)+Math.Pow(p.B-color.B,2));})!;if(combinations.Count>4096)combinations.Clear();combinations[key]=result;return result;
    }
    public static string ForNode(Proposition node,IEnumerable<Region> regions,GraphDocument? document=null)
    {
        var parents=new HashSet<string?>{node.Parent};var parent=node.Parent;
        while(document!=null&&parent!=null){parent=document.Node(parent)?.Parent;parents.Add(parent);}
        var members=regions.Where(r=>parents.Contains(r.Parent)&&r.IsAbsolute&&GraphBoard.Bounds(r).Contains(GraphStyle.Center(node))).ToArray();return members.Length==0?node.Color:Combined(members);
    }
    public readonly record struct Patch(Rect Bounds,string Color);
    public static List<Patch> Intersections(IReadOnlyList<Region> regions)
    {
        // Subdivide only overlap rectangles. This also assigns one stable color to triple overlaps.
        var result=new List<Patch>();var boxes=regions.Select(GraphBoard.Bounds).ToArray();var overlaps=new List<Rect>();
        for(int i=0;i<boxes.Length;i++)for(int j=i+1;j<boxes.Length;j++){var b=Rect.Intersect(boxes[i],boxes[j]);if(!b.IsEmpty&&b.Width>0&&b.Height>0)overlaps.Add(b);}
        if(overlaps.Count==0)return result;
        var xs=overlaps.SelectMany(b=>new[]{b.Left,b.Right}).Distinct().Order().ToArray();var ys=overlaps.SelectMany(b=>new[]{b.Top,b.Bottom}).Distinct().Order().ToArray();
        // Avoid a combinatorial paint cost with unusually many thresholds; point colors still remain exact.
        if((long)xs.Length*ys.Length>20000)return result;
        for(int x=1;x<xs.Length;x++)for(int y=1;y<ys.Length;y++){var box=new Rect(xs[x-1],ys[y-1],xs[x]-xs[x-1],ys[y]-ys[y-1]);var center=new Point(box.X+box.Width/2,box.Y+box.Height/2);var members=regions.Where((r,i)=>boxes[i].Contains(center)).ToArray();if(members.Length>1)result.Add(new Patch(box,Combined(members)));}
        return result;
    }
}
