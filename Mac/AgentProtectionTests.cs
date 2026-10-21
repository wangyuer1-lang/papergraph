using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
namespace Papergraph;

public partial class MainWindow
{
    internal async Task RunAgentProtectionTests()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        JsonElement Call(object value)=>JsonSerializer.SerializeToElement(HandleAgent(JsonSerializer.SerializeToElement(value)));
        var fixture=new GraphDocument{Title="AI protection fixture",Nodes=[new(){Id="first",Title="Original",Note="Human note"},new(){Id="second",Title="Next",X=400}],Edges=[new(){Id="edge",From="first",To="second"}],Regions=[new(){Id="frame",IsAbsolute=true,Width=1000,Height=800}]};
        ResetDocument(fixture,FreshPath("AI protection fixture"),true);Save();
        Check(!doc.AgentReadOnly&&!GraphDocument.Parse("{\"Version\":1,\"Title\":\"Legacy\",\"Nodes\":[],\"Edges\":[],\"Regions\":[]}").AgentReadOnly,"Existing graphs default to agent-writable");
        object Edit(string body)=>new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),nodes=new[]{new{id="first",body}}};
        async Task Flush(){await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);UpdateLayout();}
        Button LockButton(string path)=>libraryTree.GetVisualDescendants().OfType<Button>().Single(b=>b.Classes.Contains("graph-lock")&&b.Tag is GraphEntry entry&&SamePath(entry.Path,path));
        using var pointer=new Pointer(907,PointerType.Mouse,true);ulong time=1;
        async Task ClickLock(string path)
        {
            await Flush();var button=LockButton(path);var at=new Point(button.Bounds.Width/2,button.Bounds.Height/2);var activeBefore=file;
            button.RaiseEvent(new PointerPressedEventArgs(button,pointer,button,at,time++,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));
            Check(file==activeBefore,"Pointer-down on a lock does not switch the active graph");
            // Synthetic routed input does not update native pointer-over hit testing.
            // Cancel the simulated press outside, then dispatch the semantic click.
            button.RaiseEvent(new PointerReleasedEventArgs(button,pointer,button,new Point(-20,-20),time++,new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
            button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await Flush();
        }
        var oldEdit=Edit("Agent edit before lock");Call(oldEdit);
        var oldAdd=new{operation="addNodes",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),nearNodeId="first",nodes=new[]{new{body="Added before lock"}}};Call(oldAdd);
        await Flush();
        Check(LockButton(file).Content is AgentLockGlyph {IsLocked:false},"An unlocked graph has a visible open-lock icon");
        var titleBeforeLock=doc.Title;await ClickLock(file);
        Check(LockButton(file).Content is AgentLockGlyph {IsLocked:true}&&doc.Title==titleBeforeLock&&(AutomationProperties.GetName(LockButton(file))??"").Contains(T("AI read-only · Click to unlock")),"Clicking the inline lock changes its state and accessible action without changing the graph name");
        Check(doc.AgentReadOnly&&GraphDocument.Parse(File.ReadAllText(file)).AgentReadOnly&&CurrentGraph.DisplayTitle.Contains(T("AI read-only")),"AI permission saves durably and appears in the graph label");
        Check(GraphDocument.Parse(doc.Serialize()).AgentReadOnly,"Protection survives document serialization/reload");
        var locked=doc.Serialize();var disk=File.ReadAllText(file);var history=undo.Count;
        void Reject(object value,string message)
        {
            bool rejected=false;try{Call(value);}catch(InvalidDataException ex){rejected=ex.Message.Contains("AI read-only");}
            Check(rejected&&doc.Serialize()==locked&&File.ReadAllText(file)==disk&&undo.Count==history,message);
        }
        Reject(Edit("Forbidden edit"),"Locked graph rejects fresh edits atomically");
        Reject(oldEdit,"Locked graph rejects an earlier successful edit retry");
        Reject(oldAdd,"Locked graph rejects an earlier successful add retry");
        Reject(new{operation="addNodes",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),nearNodeId="first",nodes=new[]{new{body="Forbidden new node"}}},"Locked graph rejects new points");
        Reject(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),agentReadOnly=false,edges=new[]{new{id="edge",direction="reverse"}},regions=new[]{new{id="frame",width=1200}},nodes=new[]{new{id="first",x=99,note="Forbidden note"}}},"Payload cannot unlock a graph or change its layout, relations, frames or notes");
        foreach(var operation in new[]{"snapshot","search","fullText"})
        {
            var result=Call(new{operation,query="Original"});
            Check(result.GetProperty("agentReadOnly").GetBoolean()&&!result.GetProperty("agentWritable").GetBoolean(),"Locked graph remains readable through "+operation);
        }
        var snapshot=Call(new{operation="snapshot"});
        Check(new[]{"nodes","edges","regions"}.All(k=>snapshot.GetProperty(k).EnumerateArray().All(o=>o.GetProperty("notePages").EnumerateArray().All(p=>!p.GetProperty("agentWritable").GetBoolean()))),"All notes report non-writable permissions when the graph is protected");
        // Direct preparation helpers also enforce the guard, outside the live handler.
        bool addBlocked=false,editBlocked=false;
        try{AgentProtocol.Prepare(doc,file,JsonSerializer.SerializeToElement(oldAdd));}catch(InvalidDataException e){addBlocked=e.Message.Contains("AI read-only");}
        try{AgentGraphEdit.Prepare(doc,file,JsonSerializer.SerializeToElement(oldEdit));}catch(InvalidDataException e){editBlocked=e.Message.Contains("AI read-only");}
        Check(addBlocked&&editBlocked,"Both mutation preparation helpers enforce AI protection");
        Graph.Selected=["first"];SelectionChanged();bodyBox.Text="Manual editing is still available";Save();
        Check(doc.Node("first")!.Title=="Manual editing is still available"&&doc.AgentReadOnly,"Native human editing remains available on a protected graph");
        Undo();Check(doc.AgentReadOnly,"Undo preserves the current protection permission");
        Undo();Check(doc.AgentReadOnly,"Undo across a pre-lock snapshot cannot remove protection");
        Redo();Check(doc.AgentReadOnly,"Redo preserves protection");
        var copy=Call(new{operation="duplicateGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),title="Protected copy"});
        var copyPath=copy.GetProperty("documentPath").GetString()!;
        Check(copy.GetProperty("agentReadOnly").GetBoolean()&&GraphDocument.Parse(File.ReadAllText(copyPath)).AgentReadOnly,"A graph copy inherits protection");
        var list=Call(new{operation="listGraphs"});
        Check(list.GetProperty("graphs").EnumerateArray().Where(g=>g.GetProperty("documentPath").GetString()==file||g.GetProperty("documentPath").GetString()==copyPath).All(g=>g.GetProperty("agentReadOnly").GetBoolean()&&!g.GetProperty("agentWritable").GetBoolean()),"Library listing exposes active and inactive graph permissions");
        Check(Call(new{operation="snapshot",documentPath=copyPath}).GetProperty("agentReadOnly").GetBoolean(),"Inactive protected graphs remain readable");
        var activePath=file;var activeRevision=AgentProtocol.Revision(doc);
        await ClickLock(copyPath);
        Check(file==activePath&&AgentProtocol.Revision(doc)==activeRevision&&!GraphDocument.Parse(File.ReadAllText(copyPath)).AgentReadOnly,"Changing an inactive graph permission preserves the active manuscript");
        await ClickLock(file);
        Check(!doc.AgentReadOnly&&!GraphDocument.Parse(File.ReadAllText(file)).AgentReadOnly,"Human unlock is persisted");
        Undo();Check(!doc.AgentReadOnly,"Undo cannot re-enable a permission the user disabled");
        Call(Edit("Authorized after unlock"));Check(doc.Node("first")!.Title=="Authorized after unlock","Agent writes resume after human unlock");
        SetGraphAgentReadOnly(file,true);
        var pipeInput=Path.Combine(dataDir,"protected-write-request.json");var pipeOutput=Path.Combine(dataDir,"protected-write-response.json");
        File.WriteAllText(pipeInput,JsonSerializer.Serialize(Edit("Blocked over IPC")));
        Check(await AgentBridge.Client(dataDir,pipeInput,pipeOutput)==2,"The live agent pipe rejects protected writes");
        var failure=JsonDocument.Parse(File.ReadAllText(pipeOutput));Check(!failure.RootElement.GetProperty("ok").GetBoolean()&&failure.RootElement.GetProperty("error").GetString()!.Contains("AI read-only"),"IPC reports a clear protection error");
        SetGraphAgentReadOnly(file,false);Save();
        SetGraphAgentReadOnly(copyPath,true);await Flush();
        Check(LockButton(copyPath).Content is AgentLockGlyph {IsLocked:true}&&LockButton(file).Content is AgentLockGlyph {IsLocked:false},"Each graph displays its own lock state");
        var priorDark=dark;
        foreach(var isDark in new[]{false,true})
        {
            dark=isDark;ApplyTheme();await Flush();
            using var image=new RenderTargetBitmap(new PixelSize((int)Width,(int)Height),new Vector(96,96));image.Render(this);image.Save(Path.Combine(dataDir,isDark?"inline-lock-dark.png":"inline-lock-light.png"));
        }
        dark=priorDark;ApplyTheme();SaveSettings();
    }
}
