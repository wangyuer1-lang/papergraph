using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Threading;
namespace Papergraph;

// Local, current-user-only IPC. All document access runs on the UI dispatcher.
internal sealed class AgentBridge : IDisposable
{
    readonly CancellationTokenSource stop=new();
    internal static string PipeName(string directory)=>"papergraph-agent-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant())))[..20];
    internal AgentBridge(string directory,Dispatcher dispatcher,Func<JsonElement,object> handle)
    {
        _=Task.Run(async()=>{
            while(!stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe=new NamedPipeServerStream(PipeName(directory),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(15));
                    var json=await Read(pipe,1_000_000,deadline.Token);object result;
                    try{using var request=JsonDocument.Parse(json);result=await dispatcher.InvokeAsync(()=>handle(request.RootElement),DispatcherPriority.Background,deadline.Token);}
                    catch(Exception ex){result=new{ok=false,error=ex.Message};}
                    await Write(pipe,JsonSerializer.Serialize(result,GraphDocument.Options),deadline.Token);
                }
                catch(OperationCanceledException){}catch(IOException){}catch(Exception){if(stop.IsCancellationRequested)break;await Task.Delay(100);}
            }
        });
    }
    internal static async Task<int> Client(string directory,string input,string output)
    {
        object? error=null;
        try
        {
            if(new FileInfo(input).Length>1_000_000)throw new InvalidDataException("Request exceeds 1 MB.");
            var json=await File.ReadAllTextAsync(input);using var check=JsonDocument.Parse(json);
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var pipe=new NamedPipeClientStream(".",PipeName(directory),PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000,deadline.Token);await Write(pipe,json,deadline.Token);
            var response=await Read(pipe,32_000_000,deadline.Token);await File.WriteAllTextAsync(output,response);
            using var parsed=JsonDocument.Parse(response);return parsed.RootElement.GetProperty("ok").GetBoolean()?0:2;
        }
        catch(Exception ex){error=new{ok=false,error="Open papergraph for this data directory, then retry. "+ex.Message};}
        try{await File.WriteAllTextAsync(output,JsonSerializer.Serialize(error,GraphDocument.Options));}catch{}
        return 2;
    }
    static async Task<string> Read(Stream stream,int limit,CancellationToken token)
    {
        var header=new byte[4];await stream.ReadExactlyAsync(header,token);var count=BinaryPrimitives.ReadInt32LittleEndian(header);
        if(count<1||count>limit)throw new InvalidDataException("Invalid agent message size.");
        var bytes=new byte[count];await stream.ReadExactlyAsync(bytes,token);return Encoding.UTF8.GetString(bytes);
    }
    static async Task Write(Stream stream,string json,CancellationToken token)
    {
        var bytes=Encoding.UTF8.GetBytes(json);var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,bytes.Length);
        await stream.WriteAsync(header,token);await stream.WriteAsync(bytes,token);await stream.FlushAsync(token);
    }
    public void Dispose()=>stop.Cancel();
}

internal static class AgentProtocol
{
    internal static string Revision(GraphDocument doc)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(doc.Serialize())));
    internal static string Required(JsonElement request,string name)=>request.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(value.GetString())?value.GetString()!:throw new InvalidDataException("Missing "+name+".");
    internal static object Node(Proposition n)=>new{id=n.Id,caption=n.Caption,body=n.Title,note=n.Note,notePages=GraphNotes.Snapshot(n),kind=n.Kind,parentId=n.Parent,x=n.X,y=n.Y,color=n.Color,markColor=n.MarkColor};
    internal static (GraphDocument Document,string[] Ids,bool AlreadyApplied) Prepare(GraphDocument doc,string file,JsonElement request)
    {
        if(!string.Equals(Path.GetFullPath(Required(request,"expectedDocument")),Path.GetFullPath(file),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The active document changed. Read a fresh snapshot.");
        var requestId=Required(request,"requestId");if(!Guid.TryParse(requestId,out var requestGuid))throw new InvalidDataException("requestId must be a UUID; reuse it when retrying.");
        var anchor=doc.Node(Required(request,"nearNodeId"))??throw new InvalidDataException("nearNodeId no longer exists.");
        var source=request.GetProperty("nodes");if(source.ValueKind!=JsonValueKind.Array||source.GetArrayLength() is <1 or >100)throw new InvalidDataException("Supply 1 to 100 nodes.");
        var additions=new List<Proposition>();int index=0;
        foreach(var item in source.EnumerateArray())
        {
            string Text(string key)=>item.TryGetProperty(key,out var value)?value.GetString()??"":"";
            var id="agent-"+requestGuid.ToString("N")+"-"+index++;
            var addition=new Proposition{Id=id,Caption=Text("caption"),Title=Required(item,"body"),Parent=anchor.Parent,Color=anchor.Color};
            GraphNotes.ApplyAgent(addition,item);additions.Add(addition);
        }
        var ids=additions.Select(n=>n.Id).ToArray();var existing=additions.Select(n=>doc.Node(n.Id)).ToArray();
        if(existing.Any(n=>n!=null))
        {
            if(additions.Zip(existing).All(pair=>pair.Second is Proposition n&&n.Caption==pair.First.Caption&&n.Title==pair.First.Title&&n.Note==pair.First.Note&&JsonSerializer.Serialize(n.AdditionalNotes)==JsonSerializer.Serialize(pair.First.AdditionalNotes)&&n.Parent==pair.First.Parent))return(doc,ids,true);
            throw new InvalidDataException("This requestId was already used with different content or only some nodes remain. Inspect the graph before retrying.");
        }
        if(Required(request,"expectedRevision")!=Revision(doc))throw new InvalidDataException("Document content changed. Read a fresh snapshot before adding nodes.");
        var next=GraphDocument.Parse(doc.Serialize());
        foreach(var node in additions)
        {
            // Place beneath the reference in a compact grid; never move existing objects or create edges.
            bool found=false;
            for(int row=1;row<=600&&!found;row++)for(int col=0;col<3&&!found;col++)
            {
                node.X=anchor.X+col*300;node.Y=anchor.Y+row*170;
                found=!next.Nodes.Where(n=>n.Parent==node.Parent).Any(n=>(GraphStyle.Center(n)-GraphStyle.Center(node)).Length<110);
            }
            if(!found)throw new InvalidDataException("Could not find space near the reference point.");next.Nodes.Add(node);
        }
        next.Validate();return(next,ids,false);
    }
}
