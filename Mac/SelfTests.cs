using Avalonia;
using Avalonia.Media.Imaging;
using System.Text.Json;
using P=System.Windows.Point;
using R=System.Windows.Rect;
using V=System.Windows.Vector;
namespace Papergraph;
public partial class MainWindow
{
    internal async Task RunSelfTests()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        ModelTests.Run();AgentLibraryTests.Run();SpacingTests.Run();CaptionLayoutTests.Run();RingConnectionTests.Run();
        Console.WriteLine("PASS: existing model, spacing, caption layout and ring regression suites");
        var original=doc.Serialize();Remember();doc.Nodes[0].Title="中文の論文 🌱";Changed(false);Check(Save(),"Save modified Unicode content");
        Check(GraphDocument.Parse(File.ReadAllText(file)).Nodes[0].Title=="中文の論文 🌱","Unicode disk round trip");Undo();Check(doc.Serialize()==original,"Whole document undo");Redo();Check(doc.Nodes[0].Title=="中文の論文 🌱","Whole document redo");
        Graph.Selected=["a"];SelectionChanged();bodyBox.Text="输入中文、日本語の入力";notesBox.Text="My personal note";
        Check(doc.Node("a")!.Title==bodyBox.Text&&doc.Node("a")!.Note==notesBox.Text,"Native editors update selected proposition and personal note");
        notePages.SelectedIndex=1;notesBox.Text="Agent evidence";Check(doc.Node("a")!.AdditionalNotes[0].Body=="Agent evidence"&&doc.Node("a")!.Note=="My personal note","Note tabs keep personal and agent pages separate");
        JsonElement Call(object request)=>JsonSerializer.SerializeToElement(HandleAgent(JsonSerializer.SerializeToElement(request)),GraphDocument.Options);
        var snap=Call(new{operation="snapshot"});
        var req=new{operation="addNodes",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=snap.GetProperty("revision").GetString(),nearNodeId="a",nodes=new[]{new{body="Mac added point",note="Agent source"}}};
        var added=Call(req);var count=doc.Nodes.Count;Check(Call(req).GetProperty("alreadyApplied").GetBoolean()&&doc.Nodes.Count==count,"Agent add retry is idempotent");
        Check(File.ReadAllText(file)==doc.Serialize(),"Agent success persists before acknowledgement");
        Undo();Check(doc.Nodes.Count==count-1&&doc.Node("a")!.Note=="My personal note","One undo reverses agent batch and preserves prior human text");Redo();
        var before=doc.Serialize();bool rejected=false;
        try{Call(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision="stale",nodes=new[]{new{id="a",body="wrong"}}});}catch(InvalidDataException){rejected=true;}
        Check(rejected&&doc.Serialize()==before,"Stale agent mutation rejected without changing graph");
        var edit=new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),nodes=new[]{new{id="mac-new",body="New source",x=50,y=80}},edges=new[]{new{id="mac-edge",from="a",to="mac-new",direction="forward"}},regions=new[]{new{id="mac-frame",x=0,y=0,width=1000,height=900,body="Frame"}}};
        Call(edit);Check(doc.HasEndpoint("mac-new")&&doc.HasEndpoint("mac-frame")&&doc.Edges.Any(e=>e.Id=="mac-edge"),"Agent creates nodes, edges and frames");
        Call(edit);Check(doc.Nodes.Count(n=>n.Id=="mac-new")==1,"Graph edit retry is idempotent");
        var fragment=GraphClipboard.Capture(doc,["a","mac-new"],["mac-frame"]);var pasted=GraphClipboard.PreparePaste(doc,GraphFragment.Parse(fragment.Serialize()),new P(1200,1000));
        Check(pasted.NodeIds.Count==fragment.Graph.Nodes.Count&&pasted.RegionIds.Count==fragment.Graph.Regions.Count&&pasted.Document.Nodes.Select(n=>n.Id).Distinct().Count()==pasted.Document.Nodes.Count,"Graph clipboard remaps IDs and preserves internal connections");
        var textFixture=new GraphDocument{Nodes=[new(){Id="one",Title="输入中文、日本語の入力"},new(){Id="two",Title="Second proposition",X=400}],Edges=[new(){From="one",To="two"}]};
        var full=GraphFullText.Build(textFixture);Check(full.Pages.Count==1&&full.Pieces.Any(p=>p.Text.Contains("输入中文")),"Full-text projection includes the Unicode source proposition");
        var active=file;var create=new{operation="createGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),title="Mac second page"};
        var created=Call(create);Check(file==active&&File.Exists(created.GetProperty("documentPath").GetString()),"Agent creates background page without switching active document");Check(Call(create).GetProperty("alreadyApplied").GetBoolean(),"Page creation retry is idempotent");
        var cat=Call(new{operation="createCategory",requestId=Guid.NewGuid().ToString(),expectedLibraryRevision=catalog.Revision(),title="Mac category"});
        Call(new{operation="moveGraphs",requestId=Guid.NewGuid().ToString(),expectedLibraryRevision=catalog.Revision(),categoryId=cat.GetProperty("categoryId").GetString(),documentPaths=new[]{file}});Check(catalog.Assignments.ContainsKey(file),"Category membership persists");
        var pipeInput=Path.Combine(dataDir,"pipe-request.json");var pipeOutput=Path.Combine(dataDir,"pipe-response.json");File.WriteAllText(pipeInput,"{\"operation\":\"snapshot\"}");
        Check(await AgentBridge.Client(dataDir,pipeInput,pipeOutput)==0,"Current-user named pipe works on macOS");
        using(var reply=JsonDocument.Parse(File.ReadAllText(pipeOutput)))Check(reply.RootElement.GetProperty("documentPath").GetString()==file,"Pipe snapshot reports active graph");
        Save();
        ResetDocument(textFixture,FreshPath("Text test"),true);ToggleFullText();
        Check(graphArea.IsVisible&&textArea.IsVisible,"Full text opens below the visible graph");
        FocusTextObject("one");Check(fullTextVisible&&editorId=="one","Full text locates the source without hiding the manuscript");
        bodyBox.Text="全文编辑の検証";RefreshFullText();
        Check(doc.Node("one")!.Title=="全文编辑の検証"&&currentFullText.Text.Contains("全文编辑の検証"),"Editing the source updates live full text");
        ToggleFullText();Save();
        Check(!PathIdentity.Same("/tmp/Library/A.papergraph","/tmp/Library/a.papergraph")&&!string.Equals(AgentBridge.PipeName("/tmp/Library"),AgentBridge.PipeName("/tmp/library")),"macOS paths and IPC identities preserve case");
        await RunParityTests();
        await RunPointerParityTests();
        await RunMacUsabilityTests();
        RunIconParityTests();
        UpdateLayout();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},Avalonia.Threading.DispatcherPriority.Background);
        using(var bitmap=new RenderTargetBitmap(new PixelSize((int)Width,(int)Height),new Vector(96,96))){bitmap.Render(this);bitmap.Save(Path.Combine(dataDir,"mac-self-test.png"));}
        Check(File.Exists(Path.Combine(dataDir,"mac-self-test.png")),"Native renderer produces a complete window image");
        Console.WriteLine("Mac test evidence: "+dataDir);
    }
}
