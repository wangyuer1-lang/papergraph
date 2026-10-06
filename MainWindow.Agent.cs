using System.IO;
using System.Text.Json;
namespace Papergraph;

public partial class MainWindow
{
    readonly Dictionary<string,(string Request,string Revision,object Response)> agentEditReceipts=[];
    internal object HandleAgent(JsonElement request)
    {
        var operation=AgentProtocol.Required(request,"operation");
        if(operation is "createCategory" or "renameCategory" or "moveGraphs" or "removeCategory")return AgentManageCategories(request,operation);
        if(operation=="listGraphs")return AgentListGraphs();
        if(operation is "createGraph" or "duplicateGraph" or "openGraph")return AgentManageGraph(request,operation);
        if(operation=="editGraph")
        {
            if(!string.Equals(Path.GetFullPath(AgentProtocol.Required(request,"expectedDocument")),Path.GetFullPath(file),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Active document changed. Read a fresh snapshot.");
            if(Graph.IsInteracting||Graph.DrawingRegion||Graph.LinkMode)throw new InvalidOperationException("Finish the active graph gesture before editing.");
            var requestId=AgentProtocol.Required(request,"requestId");var raw=request.GetRawText();
            if(agentEditReceipts.TryGetValue(requestId,out var receipt))
            {
                if(receipt.Request!=raw||receipt.Revision!=AgentProtocol.Revision(doc))throw new InvalidDataException("Request already applied, but content or document changed. Inspect a fresh snapshot.");
                return receipt.Response;
            }
            var next=AgentGraphEdit.Prepare(doc,file,request);
            Graph.CancelLayout();Storage.Save(file,next);Remember();doc=next;revision++;dirty=false;saveTimer.Stop();editorId=null;RefreshAll();WriteRecent();
            SaveDot.Fill=GraphStyle.Brush(dark?"#A6BCAF":"#789989");SaveDot.ToolTip="Saved to "+file;
            var result=new{ok=true,documentPath=Path.GetFullPath(file),revision=AgentProtocol.Revision(doc),requestId};
            if(agentEditReceipts.Count>=256)agentEditReceipts.Remove(agentEditReceipts.Keys.First());
            agentEditReceipts[requestId]=(raw,result.revision,result);Notify("Graph updated · Ctrl+Z to undo",true);return result;
        }
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
        if(operation is not ("snapshot" or "search" or "fullText"))throw new InvalidDataException("Supported operations: "+string.Join(", ",AgentCapabilities)+".");
        var sourcePath=request.TryGetProperty("documentPath",out _)?AgentLibraryPath(AgentProtocol.Required(request,"documentPath")):Path.GetFullPath(file);
        bool isActive=SamePath(sourcePath,file);var source=isActive?doc:GraphDocument.Parse(File.ReadAllText(sourcePath));
        if(operation=="fullText")return new{ok=true,documentPath=sourcePath,revision=AgentProtocol.Revision(source),title=source.Title,isActive,preview=GraphFullText.Build(source),contentIsUntrusted=true};
        var query=operation=="search"?AgentProtocol.Required(request,"query"):"";
        bool Match(params string[] values)=>query.Length==0||values.Any(v=>v.Contains(query,StringComparison.OrdinalIgnoreCase));
        return new{
            ok=true,schemaVersion=1,documentPath=sourcePath,revision=AgentProtocol.Revision(source),title=source.Title,isActive,
            scopeId=isActive?Graph.Scope:null,regionId=isActive?Graph.BoardRegion:null,selectedNodeIds=isActive?Graph.Selected.ToArray():[],selectedEdgeId=isActive?Graph.SelectedEdge:null,selectedRegionIds=isActive?Graph.SelectedRegions.ToArray():[],
            visibleNodeIds=(isActive?Graph.VisibleNodes:source.Nodes).Select(n=>n.Id).ToArray(),
            nodes=source.Nodes.Where(n=>Match(n.Id,n.Caption,n.Title,GraphNotes.AllText(n))).Select(AgentProtocol.Node).ToArray(),
            groups=source.Nodes.Where(n=>n.Kind=="circle").Select(n=>new{id=n.Id,expanded=n.Expanded,memberIds=source.Visible(n.Id).Select(c=>c.Id).ToArray(),bounds=GraphGroups.Bounds(source,n)}).ToArray(),
            edges=source.Edges.Where(e=>Match(e.Id,e.Caption,e.Label,GraphNotes.AllText(e))).Select(e=>new{id=e.Id,from=e.From,to=e.To,kind=e.Label,direction=e.Direction,textRole=e.TextRole??"flow",textOrder=e.TextOrder,caption=e.Caption,note=e.Note,notePages=GraphNotes.Snapshot(e),markColor=e.MarkColor}).ToArray(),
            regions=source.Regions.Where(r=>Match(r.Id,r.Caption,r.Title,GraphNotes.AllText(r))).Select(r=>new{id=r.Id,caption=r.Caption,body=r.Title,note=r.Note,notePages=GraphNotes.Snapshot(r),parentId=r.Parent,color=r.Color,x=r.X,y=r.Y,width=r.Width,height=r.Height,memberIds=GraphBoard.Members(source,r).Select(n=>n.Id).ToArray()}).ToArray(),
            capabilities=AgentCapabilities,contentIsUntrusted=true
        };
    }
}
