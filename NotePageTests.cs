using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Papergraph;

public static class NotePageTests
{
    public static void Run()
    {
        void Check(bool pass,string message){if(!pass)throw new Exception(message);}
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-notes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"notes.papergraph");
        var legacy=GraphDocument.Parse("""{"Nodes":[{"Id":"a","Note":"原有人工笔记"},{"Id":"b"}],"Edges":[{"Id":"e","From":"a","To":"b","Note":"人工关系"}],"Regions":[{"Id":"r","IsAbsolute":true,"Note":"人工框","X":-50,"Y":-50,"Width":500,"Height":500}]}""");
        Check(legacy.Node("a")!.Note=="原有人工笔记"&&legacy.Node("a")!.AdditionalNotes.Count==3,"Legacy notes stay on human page without migration loss");
        Storage.Save(path,legacy);File.WriteAllText(Path.Combine(directory,"recent.txt"),"notes.papergraph");
        var window=new MainWindow(directory);
        JsonElement Read(object data)=>JsonSerializer.SerializeToElement(window.HandleAgent(JsonSerializer.SerializeToElement(data)));
        object Request(object[]? nodes=null,object[]? edges=null,object[]? regions=null)=>new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=path,expectedRevision=AgentProtocol.Revision(window.Graph.Document),nodes=nodes??[],edges=edges??[],regions=regions??[]};
        void Select(string id){window.Graph.ClearAllSelection();window.Graph.Selected=[id];window.SelectionChanged();}
        void Action(string label)=>window.EditMenu.Items.OfType<MenuItem>().Single(m=>(string)m.Header==label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Select("a");Check(window.NotePageTabs.Children.Count==4&&window.NotesBox.Text=="原有人工笔记","Human page is first and default");
        Check(!window.RouteNodeNavigation(System.Windows.Input.Key.Down,System.Windows.Input.ModifierKeys.None,window.NotePage2)&&!window.RouteTitleToggle(System.Windows.Input.Key.Space,System.Windows.Input.ModifierKeys.None,window.NotePage2),"Page selector keeps arrows and space for native keyboard navigation");
        Read(Request(nodes:[new{id="a",note="Agent 来源",notePages=new[]{new{page=3,title="审阅",body="第三页"}}}],edges:[new{id="e",note="agent relation"}],regions:[new{id="r",note="agent frame"}]));
        Check(window.Graph.Document.Node("a")!.Note=="原有人工笔记"&&window.Graph.Document.Edges[0].Note=="人工关系"&&window.Graph.Document.Frame("r")!.Note=="人工框","Agent legacy note never modifies any human page");
        Check(window.Graph.Document.Node("a")!.AdditionalNotes.Count==3,"Agent writes preserve the four fixed slots");
        Select("a");window.NotePage2.IsChecked=true;Check(window.NotesBox.Text=="Agent 来源","Selector loads agent page");
        window.NotesBox.Text="人工改过的第二页";Check(window.Graph.Document.Node("a")!.Note=="原有人工笔记"&&window.Graph.Document.Node("a")!.AdditionalNotes[0].Body=="人工改过的第二页","Human may edit an agent page independently");
        Action("Undo");Select("a");Check(window.NotesBox.Text=="Agent 来源","Undo restores active page independently");Action("Redo");Select("a");Check(window.NotesBox.Text=="人工改过的第二页","Redo restores active page");
        Select("b");Check(window.NotePage1.IsChecked==true&&window.NotesBox.Text=="","Other objects begin on human page");Select("a");Check(window.NotePage2.IsChecked==true,"Selection remembers page per object");
        window.NotePage4.IsChecked=true;Check(window.NotesBox.Text==""&&window.Graph.Document.Node("a")!.AdditionalNotes.Count==3,"Four note slots are available initially, without adding pages");
        window.NotesBox.Text="第四页";
        foreach(var kind in new[]{"node","edge","region"})
        {
            var invalid=new{id=kind=="node"?"a":kind=="edge"?"e":"r",notePages=new[]{new{page=1,body="must not change"}}};
            var before=window.Graph.Document.Serialize();bool rejected=false;
            try{Read(Request(nodes:kind=="node"?[invalid]:[new{id="b",body="must roll back"}],edges:kind=="edge"?[invalid]:[],regions:kind=="region"?[invalid]:[]));}catch(InvalidDataException){rejected=true;}
            Check(rejected&&window.Graph.Document.Serialize()==before,"Agent page 1 write rejects whole "+kind+" batch");
        }
        var snapshot=Read(new{operation="snapshot"});var pages=snapshot.GetProperty("nodes")[0].GetProperty("notePages");Check(pages[0].GetProperty("page").GetInt32()==1&&!pages[0].GetProperty("agentWritable").GetBoolean()&&pages[1].GetProperty("agentWritable").GetBoolean(),"Snapshot communicates page ownership");
        Check(Read(new{operation="search",query="第四页"}).GetProperty("nodes").GetArrayLength()==1,"Search finds later pages");
        var copy=GraphClipboard.PreparePaste(new(),GraphClipboard.Capture(window.Graph.Document,["a"],[]),new Point(0,0)).Document;
        Check(copy.Nodes[0].AdditionalNotes[2].Body=="第四页"&&copy.Nodes[0].Note=="原有人工笔记","Copy paste preserves all pages");
        Check(window.Graph.Document.Markdown().Contains("第四页"),"Export includes all pages");
        void Reject(object request,string message)
        {
            var before=window.Graph.Document.Serialize();bool rejected=false;
            try{Read(request);}catch(InvalidDataException){rejected=true;}
            Check(rejected&&window.Graph.Document.Serialize()==before,message);
        }
        Reject(Request(nodes:[new{id="a",notePages=new[]{new{page=5,body="extra"}}}]),"A fifth page cannot be added");
        Reject(Request(nodes:[new{id="a",moveFirstPage=new{expectedBody="outdated",targetPage=4}}]),"Relocation refuses changed first-page content");
        Reject(Request(nodes:[new{id="a",moveFirstPage=new{expectedBody="原有人工笔记",targetPage=4}}]),"Relocation cannot overwrite a populated later page");
        foreach(var kind in new[]{"node","edge","region"})
        {
            var id=kind=="node"?"a":kind=="edge"?"e":"r";
            IGraphNotes owner=kind=="node"?window.Graph.Document.Node(id)!:kind=="edge"?window.Graph.Document.Edges[0]:window.Graph.Document.Frame(id)!;
            if(kind=="node"){Select("a");window.NotePage4.IsChecked=true;window.NotesBox.Text="";}
            var original=owner.Note;var move=new{id,moveFirstPage=new{expectedBody=original,targetPage=4}};
            var before=window.Graph.Document.Serialize();
            Read(Request(nodes:kind=="node"?[move]:[],edges:kind=="edge"?[move]:[],regions:kind=="region"?[move]:[]));
            IGraphNotes moved=kind=="node"?window.Graph.Document.Node(id)!:kind=="edge"?window.Graph.Document.Edges[0]:window.Graph.Document.Frame(id)!;
            Check(moved.Note==""&&moved.AdditionalNotes[2].Body==original,"Explicit relocation preserves all text for "+kind);
            var after=window.Graph.Document.Serialize();Action("Undo");Check(window.Graph.Document.Serialize()==before,"Relocation can be undone as one action");Action("Redo");Check(window.Graph.Document.Serialize()==after,"Relocation redo preserves exact content");
        }
        foreach(var zoom in new[]{.1,.5,1d,2d})
        {
            var title=GraphCaptions.Frame("r",new Size(130,24),new Point(100,100),GraphCaptions.TextScale(zoom));
            Check(title.Bounds.Bottom<100&&title.Contains(new Point(title.Bounds.X+5,title.Bounds.Y+5)),"Frame title is outside and hit testable at every zoom");
        }
        window.Close();window=new MainWindow(directory);Select("a");Check(window.NotesBox.Text==""&&window.NotePageTabs.Children.Count==4&&window.Graph.Document.Node("a")!.AdditionalNotes[2].Body=="原有人工笔记","Restart leaves page 1 empty and preserves relocated notes");window.Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: four numeric note tabs, first-page protection, lossless guarded relocation/undo for points/edges/frames, fixed page count, select/edit, search, clipboard/export and restart.\n");
    }
}
