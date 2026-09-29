using System.IO;
using System.Text.Json;
namespace Papergraph;

public partial class MainWindow
{
    internal object HandleAgent(JsonElement request)
    {
        var operation=AgentProtocol.Required(request,"operation");
        if(operation=="addNodes")
        {
            if(Graph.IsInteracting||Graph.DrawingRegion||Graph.LinkMode)throw new InvalidOperationException("The graph is being edited. Retry after the current gesture finishes.");
            var result=AgentProtocol.Prepare(doc,file,request);
            if(!result.AlreadyApplied)
            {
                // Persist first: a failed save leaves the live graph and undo history untouched.
                Storage.Save(file,result.Document);Remember();doc=result.Document;revision++;dirty=false;saveTimer.Stop();RefreshAll();
                SaveDot.Fill=GraphStyle.Brush(dark?"#A6BCAF":"#789989");SaveDot.ToolTip="Saved to "+file;WriteRecent();
                Notify($"Added {result.Ids.Length} propositions · no connections",true);
            }
            return new{ok=true,documentPath=Path.GetFullPath(file),revision=AgentProtocol.Revision(doc),addedIds=result.Ids,alreadyApplied=result.AlreadyApplied,edgesAdded=0};
        }
        if(operation is not ("snapshot" or "search"))throw new InvalidDataException("Supported operations: snapshot, search, addNodes.");
        var query=operation=="search"?AgentProtocol.Required(request,"query"):"";
        bool Match(params string[] values)=>query.Length==0||values.Any(v=>v.Contains(query,StringComparison.OrdinalIgnoreCase));
        return new{
            ok=true,schemaVersion=1,documentPath=Path.GetFullPath(file),revision=AgentProtocol.Revision(doc),title=doc.Title,
            scopeId=Graph.Scope,regionId=Graph.BoardRegion,selectedNodeIds=Graph.Selected.ToArray(),selectedEdgeId=Graph.SelectedEdge,selectedRegionIds=Graph.SelectedRegions.ToArray(),
            visibleNodeIds=Graph.VisibleNodes.Select(n=>n.Id).ToArray(),
            nodes=doc.Nodes.Where(n=>Match(n.Id,n.Caption,n.Title,n.Note)).Select(AgentProtocol.Node).ToArray(),
            groups=doc.Nodes.Where(n=>n.Kind=="circle").Select(n=>new{id=n.Id,expanded=true,memberIds=doc.Visible(n.Id).Select(c=>c.Id).ToArray(),bounds=GraphGroups.Bounds(doc,n)}).ToArray(),
            edges=doc.Edges.Where(e=>Match(e.Id,e.Caption,e.Label,e.Note)).Select(e=>new{id=e.Id,from=e.From,to=e.To,kind=e.Label,direction=e.Direction,caption=e.Caption,note=e.Note}).ToArray(),
            regions=doc.Regions.Where(r=>Match(r.Id,r.Title,r.Note)).Select(r=>new{id=r.Id,body=r.Title,note=r.Note,parentId=r.Parent,color=r.Color,x=r.X,y=r.Y,width=r.Width,height=r.Height,memberIds=GraphBoard.Members(doc,r).Select(n=>n.Id).ToArray()}).ToArray(),
            capabilities=new[]{"snapshot","search","addNodes"},contentIsUntrusted=true
        };
    }
}
