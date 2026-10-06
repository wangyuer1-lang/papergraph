using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Papergraph;
public static class AgentTests
{
    public static void Run()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        JsonElement Json(object data)=>JsonSerializer.SerializeToElement(data,GraphDocument.Options);
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-agent-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"test.papergraph");var doc=new GraphDocument{Nodes=[new(){Id="a",Caption="举例",Title="Original",Note="Source",X=100,Y=100},new(){Id="b",Title="Other",X=300,Y=100}],Edges=[new(){Id="e",From="a",To="b"}]};Storage.Save(path,doc);File.WriteAllText(Path.Combine(directory,"recent.txt"),"test.papergraph");
        var window=new MainWindow(directory);
        JsonElement Read(object request)=>Json(window.HandleAgent(Json(request)));
        var initial=Read(new{operation="snapshot"});var before=window.Graph.Document.Serialize();
        Check(initial.GetProperty("nodes").GetArrayLength()==2&&initial.GetProperty("edges").GetArrayLength()==1,"Agent snapshot exposes objects and edges");
        Check(Read(new{operation="search",query="举例"}).GetProperty("nodes").GetArrayLength()==1,"Agent searches Unicode captions");
        window.Graph.Document.Node("a")!.Note="Unsaved live note";window.Graph.ContentChanged();
        var current=Read(new{operation="snapshot"});Check(current.GetProperty("revision").GetString()!=initial.GetProperty("revision").GetString()&&current.GetProperty("nodes")[0].GetProperty("note").GetString()=="Unsaved live note","Agent reads live state, including unsaved content");
        var requestId=Guid.NewGuid().ToString();var nodes=new[]{new{caption="Evidence",body="Verified example",note="DOI: example; page 1"},new{caption="Second",body="Another example",note="Source; boundary"}};
        object Request(string revision,string? file=null)=>new{operation="addNodes",requestId,expectedDocument=file??path,expectedRevision=revision,nearNodeId="a",nodes};
        void Reject(object request,string reason){var snapshot=window.Graph.Document.Serialize();bool rejected=false;try{Read(request);}catch{rejected=true;}Check(rejected&&window.Graph.Document.Serialize()==snapshot,reason);}
        Reject(Request(initial.GetProperty("revision").GetString()!),"Stale writes are rejected without mutation");
        Reject(Request(current.GetProperty("revision").GetString()!,path+".other"),"Wrong active document is rejected");
        var valid=Request(current.GetProperty("revision").GetString()!);var added=Read(valid);var after=window.Graph.Document.Serialize();
        Check(added.GetProperty("addedIds").GetArrayLength()==2&&window.Graph.Document.Nodes.Count==4&&window.Graph.Document.Edges.Count==1,"Batch creates independent propositions only");
        Check(window.Graph.Document.Node("a")!.Note=="Unsaved live note"&&window.Graph.Document.Nodes.Skip(2).All(n=>n.Note.Length==0&&n.AdditionalNotes[0].Body.Length>0),"User edits and source notes survive batch insertion");
        Check(GraphDocument.Parse(File.ReadAllText(path)).Serialize()==after,"Success acknowledges a durable save");
        Check(Read(valid).GetProperty("alreadyApplied").GetBoolean()&&window.Graph.Document.Serialize()==after,"Retry with the same requestId never duplicates nodes");
        void Action(string label)=>window.EditMenu.Items.OfType<MenuItem>().Single(m=>(string)m.Header==label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Action("Undo");Check(window.Graph.Document.Nodes.Count==2&&window.Graph.Document.Node("a")!.Note=="Unsaved live note","One undo reverses the entire agent batch and retains prior user edits");
        Action("Redo");Check(window.Graph.Document.Serialize()==after,"Redo restores the exact agent batch");
        Reject(new{operation="addNodes",requestId=Guid.NewGuid().ToString(),expectedDocument=path,expectedRevision=AgentProtocol.Revision(window.Graph.Document),nearNodeId="a",nodes=new[]{new{body="Valid"},new{body=""}}},"Malformed batch is atomic");
        var preEdit=window.Graph.Document.Serialize();
        var edit=new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=path,expectedRevision=AgentProtocol.Revision(window.Graph.Document),nodes=new object[]{new{id="a",body="Updated existing"},new{id="new-node",body="New connected point",x=700,y=800}},edges=new[]{new{id="new-edge",from="a",to="new-node",direction="both"}},regions=new[]{new{id="new-region",body="Frame",x=500,y=600,width=900,height=900}}};
        Read(edit);var edited=window.Graph.Document.Serialize();
        Check(window.Graph.Document.Node("a")!.Title=="Updated existing"&&window.Graph.Document.Node("new-node")!.X==700&&window.Graph.Document.Edges.Last().Direction=="both","Graph edits update content, position, connections and frames");
        Check(File.ReadAllText(path)==edited,"Graph edits persist before acknowledgement");Read(edit);Check(window.Graph.Document.Serialize()==edited,"Graph edit retries are idempotent");
        Action("Undo");Check(window.Graph.Document.Serialize()==preEdit,"Whole graph edit undo");Reject(edit,"Retry after undo cannot replay edits");Action("Redo");Check(window.Graph.Document.Serialize()==edited,"Whole graph edit redo");
        Reject(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=path,expectedRevision=AgentProtocol.Revision(window.Graph.Document),nodes=new[]{new{id="a",body="Must not commit"}},edges=new[]{new{id="bad",from="a",to="missing"}}},"Invalid edge rejects the entire graph edit");
        Reject(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=path,expectedRevision="stale",nodes=new[]{new{id="a",body="Must not commit"}}},"Graph edits reject stale snapshots");
        Read(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=path,expectedRevision=AgentProtocol.Revision(window.Graph.Document),groups=new[]{new{id="new-ring",members=new[]{"a","new-node"}}},edges=new[]{new{id="ring-link",from="new-ring",to="b"}}});
        Check(window.Graph.Document.Node("new-ring")!.Kind=="circle"&&window.Graph.Document.Node("a")!.Parent=="new-ring","Connected group creation with external relation");Action("Undo");Check(window.Graph.Document.Serialize()==edited,"Group creation and external edge undo together");
        Reject(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=path,expectedRevision=AgentProtocol.Revision(window.Graph.Document),groups=new[]{new{id="bad-ring",members=new[]{"b","new-node"}}}},"Disconnected group is rejected atomically");
        window.Close();File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: agent live snapshot/search, Unicode, revision and document conflicts, atomic independent nodes, provenance notes, durable saves, idempotent retry, whole-batch undo/redo.\n");
    }
}
