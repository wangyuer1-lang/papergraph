using System.IO;
using System.Text.Json;
using System.Windows;
namespace Papergraph;

public sealed class GraphFragment
{
    public string Format {get;set;}="papergraph-fragment";
    public int Version {get;set;}=1;
    public GraphDocument Graph {get;set;}=new();
    public string Serialize()=>JsonSerializer.Serialize(this,GraphDocument.Options);

    public static GraphFragment Parse(string json)
    {
        if(json.Length>20_000_000)throw new InvalidDataException("The copied graph exceeds 20 MB.");
        var fragment=JsonSerializer.Deserialize<GraphFragment>(json,GraphDocument.Options);
        if(fragment==null||fragment.Format!="papergraph-fragment"||fragment.Version!=1||fragment.Graph==null)
            throw new InvalidDataException("The clipboard does not contain a papergraph selection.");
        fragment.Graph.Validate();
        if(fragment.Graph.Nodes.Count+fragment.Graph.Regions.Count==0)
            throw new InvalidDataException("The copied selection is empty.");
        return fragment;
    }
}

public static class GraphClipboard
{
    public sealed record PasteResult(GraphDocument Document,HashSet<string> NodeIds,HashSet<string> RegionIds,Rect Bounds);

    public static GraphFragment Capture(GraphDocument document,IEnumerable<string> selectedNodes,IEnumerable<string> selectedRegions)
    {
        // Work on a detached snapshot: copying must never normalize or mutate the source.
        var source=GraphDocument.Parse(document.Serialize());source.MaterializeRegions();
        var nodes=selectedNodes.Where(id=>source.Node(id)!=null).ToHashSet();
        var regions=selectedRegions.Where(id=>source.Regions.Any(r=>r.Id==id)).ToHashSet();
        bool changed;
        do
        {
            int count=nodes.Count+regions.Count;
            nodes=source.Descendants(nodes);
            foreach(var region in source.Regions)
                if(region.Parent!=null&&nodes.Contains(region.Parent))regions.Add(region.Id);
            foreach(var region in source.Regions.Where(r=>regions.Contains(r.Id)).ToArray())
            {
                nodes.UnionWith(GraphBoard.Members(source,region).Select(n=>n.Id));
                regions.UnionWith(GraphBoard.InnerRegions(source,region).Select(r=>r.Id));
            }
            changed=nodes.Count+regions.Count!=count;
        }while(changed);
        if(nodes.Count+regions.Count==0)throw new InvalidOperationException("Select a point, ring, or frame to copy.");

        var graph=new GraphDocument
        {
            Nodes=source.Nodes.Where(n=>nodes.Contains(n.Id)).ToList(),
            Edges=source.Edges.Where(e=>(nodes.Contains(e.From)||regions.Contains(e.From))&&(nodes.Contains(e.To)||regions.Contains(e.To))).ToList(),
            Regions=source.Regions.Where(r=>regions.Contains(r.Id)).ToList()
        };
        foreach(var node in graph.Nodes)
        {
            if(node.Parent!=null&&!nodes.Contains(node.Parent))node.Parent=null;
            node.RegionMembership=(node.RegionMembership??[]).Where(p=>regions.Contains(p.Key))
                .ToDictionary(p=>p.Key,p=>p.Value.Where(nodes.Contains).ToList());
        }
        foreach(var region in graph.Regions)
            if(region.Parent!=null&&!nodes.Contains(region.Parent))region.Parent=null;
        graph.Validate();return new GraphFragment{Graph=graph};
    }

    public static Rect Bounds(GraphDocument graph)
    {
        var bounds=Rect.Empty;
        foreach(var n in graph.Nodes)bounds.Union(n.Kind=="circle"?GraphGroups.Bounds(graph,n):GraphStyle.Bounds(n));
        foreach(var r in graph.Regions)bounds.Union(GraphBoard.Bounds(r));
        return bounds;
    }

    public static PasteResult PreparePaste(GraphDocument destination,GraphFragment fragment,Point position,string? parent=null,Rect? board=null)
    {
        if(!double.IsFinite(position.X)||!double.IsFinite(position.Y))throw new InvalidOperationException("Invalid paste position.");
        if(parent!=null&&destination.Node(parent)?.Kind!="circle")throw new InvalidOperationException("This board no longer exists.");
        var copy=GraphFragment.Parse(fragment.Serialize()).Graph;var bounds=Bounds(copy);
        var delta=position-bounds.TopLeft;
        if(board is Rect limit)
        {
            if(bounds.Width>limit.Width||bounds.Height>limit.Height)
                throw new InvalidOperationException("The copied selection is larger than this board. Resize the board or paste in the overview.");
            delta=GraphBoard.ClampDelta(bounds,limit,delta);
        }
        var ids=copy.Nodes.Select(n=>n.Id).Concat(copy.Edges.Select(e=>e.Id)).Concat(copy.Regions.Select(r=>r.Id))
            .ToDictionary(id=>id,_=>Guid.NewGuid().ToString("N"));
        foreach(var n in copy.Nodes)
        {
            n.Id=ids[n.Id];n.Parent=n.Parent==null?parent:ids[n.Parent];n.X+=delta.X;n.Y+=delta.Y;
            n.RegionMembership=(n.RegionMembership??[]).Where(p=>ids.ContainsKey(p.Key))
                .ToDictionary(p=>ids[p.Key],p=>p.Value.Where(ids.ContainsKey).Select(id=>ids[id]).ToList());
        }
        foreach(var e in copy.Edges){e.Id=ids[e.Id];e.From=ids[e.From];e.To=ids[e.To];}
        foreach(var r in copy.Regions)
        {
            r.Id=ids[r.Id];r.Parent=r.Parent==null?parent:ids[r.Parent];r.X+=delta.X;r.Y+=delta.Y;
            r.Members=r.Members.Select(id=>ids[id]).ToList();
        }
        // Validate the full result before the caller adds an undo entry or changes the live graph.
        var result=GraphDocument.Parse(destination.Serialize());
        result.Nodes.AddRange(copy.Nodes);result.Edges.AddRange(copy.Edges);result.Regions.AddRange(copy.Regions);result.Validate();
        bounds.Offset(delta);
        return new(result,copy.Nodes.Select(n=>n.Id).ToHashSet(),copy.Regions.Select(r=>r.Id).ToHashSet(),bounds);
    }
}

internal interface IGraphClipboardStore
{
    string? Read();
    void Write(string json);
}

internal sealed class SystemGraphClipboardStore : IGraphClipboardStore
{
    const string DataFormat="Papergraph.GraphFragment.v1";
    public string? Read()
    {
        var data=Clipboard.GetDataObject();
        if(data?.GetDataPresent(DataFormat,false)==true)return data.GetData(DataFormat,false) as string;
        return data?.GetDataPresent(DataFormats.UnicodeText,false)==true?data.GetData(DataFormats.UnicodeText,false) as string:null;
    }
    public void Write(string json)
    {
        var data=new DataObject();data.SetData(DataFormat,json);data.SetText(json,TextDataFormat.UnicodeText);
        Clipboard.SetDataObject(data,true);
    }
}
