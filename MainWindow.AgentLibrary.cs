using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Papergraph;

public partial class MainWindow
{
    static readonly string[] AgentCapabilities=["snapshot","search","fullText","addNodes","editGraph","listGraphs","createGraph","duplicateGraph","openGraph","createCategory","renameCategory","moveGraphs","removeCategory"];
    readonly Dictionary<string,(string Hash,string Path,string Revision)> agentOpenReceipts=[];
    sealed record AgentPageReceipt(string Hash,string Operation,string Path,string CreatedRevision);

    // An agent can address sidebar documents, not arbitrary filesystem paths.
    string AgentLibraryPath(string path)
    {
        if(!Path.IsPathFullyQualified(path))throw new InvalidDataException("Use an absolute documentPath from listGraphs.");
        var full=Path.GetFullPath(path);
        if(SamePath(full,file))return Path.GetFullPath(file);
        return library.FirstOrDefault(e=>SamePath(e.Path,full))?.Path
            ??throw new InvalidDataException("This graph is not in the library. Use a documentPath from listGraphs.");
    }

    object AgentListGraphs()
    {
        var entries=new List<object>();
        foreach(var entry in library)
        {
            bool active=SamePath(entry.Path,file);
            try
            {
                var document=active?doc:GraphDocument.Parse(File.ReadAllText(entry.Path));
                entries.Add(new{documentPath=entry.Path,categoryId=catalog.Assignments.GetValueOrDefault(entry.Path),title=document.Title,isActive=active,available=true,
                    revision=AgentProtocol.Revision(document),nodeCount=document.Nodes.Count,edgeCount=document.Edges.Count,regionCount=document.Regions.Count});
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {entries.Add(new{documentPath=entry.Path,categoryId=catalog.Assignments.GetValueOrDefault(entry.Path),title=entry.Title,isActive=active,available=false,error=ex.Message});}
        }
        return new{ok=true,activeDocumentPath=Path.GetFullPath(file),activeRevision=AgentProtocol.Revision(doc),graphs=entries,libraryRevision=catalog.Revision(),categories=catalog.Categories.Select(c=>new{id=c.Id,title=c.Name}).ToArray(),capabilities=AgentCapabilities,contentIsUntrusted=true};
    }

    void AgentCheckActive(JsonElement request)
    {
        if(!SamePath(AgentProtocol.Required(request,"expectedDocument"),file))throw new InvalidDataException("Active document changed. Read a fresh snapshot.");
        if(AgentProtocol.Required(request,"expectedRevision")!=AgentProtocol.Revision(doc))throw new InvalidDataException("Document changed. Read a fresh snapshot.");
        if(Graph.IsInteracting||Graph.DrawingRegion||Graph.LinkMode)throw new InvalidOperationException("Finish the active graph gesture before changing pages.");
    }

    void AgentSaveCurrent()
    {
        Graph.CancelLayout();
        if(!Save())throw new IOException("Could not save the current graph. It is still open and unchanged.");
        saveTimer.Stop();
    }

    void AgentTrackPage(string path,GraphDocument document)
    {
        if(!library.Any(e=>SamePath(e.Path,path)))library.Add(new(Path.GetFullPath(path),DisplayTitle(document.Title)));
        TrackCurrentGraph();
    }

    object AgentPageResult(string path,string requestId,bool alreadyApplied)
    {
        bool active=SamePath(path,file);var document=active?doc:GraphDocument.Parse(File.ReadAllText(path));
        return new{ok=true,requestId,alreadyApplied,documentPath=Path.GetFullPath(path),revision=AgentProtocol.Revision(document),title=document.Title,isActive=active,
            activeDocumentPath=Path.GetFullPath(file),activeRevision=AgentProtocol.Revision(doc)};
    }

    static string AgentPageTitle(JsonElement request,string fallback)
    {
        var title=request.TryGetProperty("title",out _)?AgentProtocol.Required(request,"title"):fallback;
        if(title.Length>500)throw new InvalidDataException("A page title supports up to 500 characters.");
        return title;
    }

    object AgentManageGraph(JsonElement request,string operation)
    {
        var requestId=AgentProtocol.Required(request,"requestId");
        if(!Guid.TryParse(requestId,out var guid))throw new InvalidDataException("requestId must be a UUID; reuse the same request when retrying.");
        var key=guid.ToString("N");var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.GetRawText())));
        var receiptPath=Path.Combine(dataDir,"agent-requests",key+".json");
        var destination=Path.GetFullPath(Path.Combine(dataDir,"agent-"+key+".papergraph"));

        // A completed creation is never replayed, even after restarting or switching pages.
        // Retries report its current state without opening it again or overwriting later edits.
        if(File.Exists(receiptPath))
        {
            var receipt=JsonSerializer.Deserialize<AgentPageReceipt>(File.ReadAllText(receiptPath))??throw new InvalidDataException("Invalid page receipt.");
            if(receipt.Hash!=hash||receipt.Operation!=operation||!SamePath(receipt.Path,destination))throw new InvalidDataException("requestId was already used for a different page operation.");
            if(!File.Exists(destination))throw new InvalidDataException("The previously created page was moved or removed. Inspect listGraphs; use a new requestId only for an intentional new page.");
            var saved=SamePath(destination,file)?doc:GraphDocument.Parse(File.ReadAllText(destination));
            AgentTrackPage(destination,saved);return AgentPageResult(destination,requestId,true);
        }
        if(agentOpenReceipts.TryGetValue(key,out var opened))
        {
            if(opened.Hash!=hash||!SamePath(opened.Path,file)||opened.Revision!=AgentProtocol.Revision(doc))throw new InvalidDataException("This page request was already applied; the active page or content changed. Read a fresh snapshot.");
            return AgentPageResult(file,requestId,true);
        }

        AgentCheckActive(request);
        if(operation=="openGraph")
        {
            var target=AgentLibraryPath(AgentProtocol.Required(request,"documentPath"));
            var loaded=SamePath(target,file)?doc:GraphDocument.Parse(File.ReadAllText(target));
            if(AgentProtocol.Required(request,"expectedTargetRevision")!=AgentProtocol.Revision(loaded))throw new InvalidDataException("Target graph changed. Read its snapshot or listGraphs again.");
            AgentSaveCurrent();
            if(!SamePath(target,file))ResetDocument(loaded,target,false);
            if(agentOpenReceipts.Count>=256)agentOpenReceipts.Remove(agentOpenReceipts.Keys.First());
            agentOpenReceipts[key]=(hash,Path.GetFullPath(file),AgentProtocol.Revision(doc));
            return AgentPageResult(file,requestId,false);
        }

        bool activate=false;
        if(request.TryGetProperty("activate",out var value))
        {
            if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidDataException("activate must be true or false.");
            activate=value.GetBoolean();
        }
        GraphDocument next;
        if(operation=="duplicateGraph")
        {
            var sourcePath=request.TryGetProperty("sourceDocument",out _)?AgentLibraryPath(AgentProtocol.Required(request,"sourceDocument")):file;
            var source=SamePath(sourcePath,file)?doc:GraphDocument.Parse(File.ReadAllText(sourcePath));
            if(!SamePath(sourcePath,file)&&AgentProtocol.Required(request,"expectedSourceRevision")!=AgentProtocol.Revision(source))throw new InvalidDataException("Source graph changed. Read its snapshot again.");
            next=GraphDocument.Parse(source.Serialize());next.Title=AgentPageTitle(request,DisplayTitle(source.Title)+" (copy)");
        }
        else next=new(){Title=AgentPageTitle(request,AgentProtocol.Required(request,"title"))};
        next.Validate();
        if(File.Exists(destination))throw new InvalidDataException("A page already exists for this requestId but its receipt is unavailable: "+destination+". Inspect it before retrying; it will not be overwritten.");
        AgentSaveCurrent();
        Storage.SaveNew(destination,next);
        // Persist before acknowledging, so retries across app restarts remain idempotent.
        var record=new AgentPageReceipt(hash,operation,destination,AgentProtocol.Revision(next));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            var pending=receiptPath+".tmp";
            using(var stream=new FileStream(pending,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
            {var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record));stream.Write(bytes);stream.Flush(true);}
            File.Move(pending,receiptPath);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            AgentTrackPage(destination,next);
            throw new IOException("Page was saved at "+destination+", but the retry receipt could not be saved. Inspect listGraphs before making another request. "+ex.Message,ex);
        }
        if(activate)ResetDocument(next,destination,false);else AgentTrackPage(destination,next);
        Notify(operation=="duplicateGraph"?"Graph copied to a new page":"New graph created");
        return AgentPageResult(destination,requestId,false);
    }
}
