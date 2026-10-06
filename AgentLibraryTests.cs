using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Papergraph;

public static class AgentLibraryTests
{
    public static void Run()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-agent-pages-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var first=Path.Combine(directory,"Example.papergraph");
        var original=new GraphDocument{Title="Original 中文",Nodes=[
            new(){Id="ring",Kind="circle",Expanded=true,Caption="Group",Title="Group body",Note="Group note",X=400,Y=300},
            new(){Id="a",Parent="ring",Caption="命题",Title="Original (Author, 2026).",Note="Original note",X=100,Y=200,MarkColor="#E5484D"},
            new(){Id="b",Parent="ring",Title="Second",X=500,Y=200},new(){Id="outside",Title="Outside",X=900,Y=600}],
            Edges=[new(){Id="ab",From="a",To="b",Direction="both",Caption="Relation",Note="Edge note",MarkColor="#E5484D"},new(){Id="out",From="ring",To="outside",Direction="reverse"}],
            Regions=[new(){Id="frame",IsAbsolute=true,Title="Frame",Note="Frame note",Color="#579A88",X=0,Y=0,Width=1500,Height=1200}]};
        Storage.Save(first,original);
        var otherPath=Path.Combine(directory,"Other.papergraph");Storage.Save(otherPath,new(){Title="Other",Nodes=[new(){Id="q",Title="Inactive evidence"}]});
        var invalidPath=Path.Combine(directory,"Invalid.papergraph");Storage.Save(invalidPath,new(){Title="Will become invalid"});
        var window=new MainWindow(directory);
        JsonElement Read(object request)=>JsonSerializer.SerializeToElement(window.HandleAgent(JsonSerializer.SerializeToElement(request,GraphDocument.Options)),GraphDocument.Options);
        JsonObject Mutation(string operation)=>new(){["operation"]=operation,["requestId"]=Guid.NewGuid().ToString(),["expectedDocument"]=Read(new{operation="snapshot"}).GetProperty("documentPath").GetString(),["expectedRevision"]=AgentProtocol.Revision(window.Graph.Document)};
        void Reject(object request,string message)
        {
            var json=window.Graph.Document.Serialize();var current=(window.CurrentGraph).Path;var count=Directory.GetFiles(directory,"*.papergraph").Length;bool rejected=false;
            try{Read(request);}catch{rejected=true;}
            Check(rejected&&window.Graph.Document.Serialize()==json&&(window.CurrentGraph).Path==current&&Directory.GetFiles(directory,"*.papergraph").Length==count,message);
        }

        window.Graph.Selected=["a"];window.SelectionChanged();window.NotesBox.Text="Pending note 中文";
        var live=Read(new{operation="snapshot"});
        var listed=Read(new{operation="listGraphs"});
        Check(listed.GetProperty("graphs").GetArrayLength()==3&&listed.GetProperty("activeRevision").GetString()==live.GetProperty("revision").GetString(),"List exposes live current revision and all sidebar pages");
        Check(Read(new{operation="snapshot",documentPath=otherPath}).GetProperty("isActive").GetBoolean()==false,"Snapshot can read inactive pages");
        Check(Read(new{operation="search",documentPath=otherPath,query="Inactive"}).GetProperty("nodes").GetArrayLength()==1&&window.Graph.Selected.SetEquals(["a"])&&window.NotesBox.Text=="Pending note 中文","Inactive reads leave current editor and selection untouched");
        var unknown=Path.Combine(Path.GetTempPath(),"unknown-"+Guid.NewGuid()+".papergraph");Storage.Save(unknown,new(){Title="Not in sidebar"});
        Reject(new{operation="snapshot",documentPath=unknown},"Unknown paths are rejected");
        File.WriteAllText(invalidPath,"invalid");
        Check(Read(new{operation="listGraphs"}).GetProperty("graphs").EnumerateArray().Single(g=>g.GetProperty("documentPath").GetString()==invalidPath).GetProperty("available").GetBoolean()==false,"Unreadable page does not hide other library entries");

        var stale=Mutation("createGraph");stale["title"]="Bad";stale["expectedRevision"]="stale";Reject(stale,"Stale create is rejected");
        var blank=Mutation("createGraph");blank["title"]=" ";Reject(blank,"Blank title is rejected before saving or creating");
        var invalidFlag=Mutation("createGraph");invalidFlag["title"]="Bad";invalidFlag["activate"]="true";Reject(invalidFlag,"Invalid activate type is rejected");
        var gesture=Mutation("createGraph");gesture["title"]="Busy";window.Graph.LinkMode=true;Reject(gesture,"Page operations reject active linking gestures");window.Graph.LinkMode=false;
        var failedSave=Mutation("createGraph");failedSave["title"]="Must wait for saving";
        Directory.CreateDirectory(first+".tmp");Reject(failedSave,"A failed current save leaves page and editor intact and creates no destination");Directory.Delete(first+".tmp");

        var create=Mutation("createGraph");create["title"]="New page 新页面";
        var created=Read(create);var createdPath=created.GetProperty("documentPath").GetString()!;
        Check(!created.GetProperty("isActive").GetBoolean()&&window.Graph.Document.Title==original.Title&&GraphDocument.Parse(File.ReadAllText(createdPath)).Nodes.Count==0,"Create writes an empty named page without changing current page");
        Check(GraphDocument.Parse(File.ReadAllText(first)).Node("a")!.Note=="Pending note 中文","Create saves pending editor text first");
        Check(window.Graph.Selected.SetEquals(["a"])&&Read(create).GetProperty("alreadyApplied").GetBoolean(),"Create retry neither duplicates nor changes selection");
        var conflict=create.DeepClone().AsObject();conflict["title"]="Another";Reject(conflict,"UUID reuse with altered content is rejected");

        var duplicate=Mutation("duplicateGraph");duplicate["title"]="Full copy";duplicate["activate"]=true;
        var expectedCopy=GraphDocument.Parse(window.Graph.Document.Serialize());expectedCopy.Title="Full copy";
        var copied=Read(duplicate);var copiedPath=copied.GetProperty("documentPath").GetString()!;
        Check(copied.GetProperty("isActive").GetBoolean()&&window.Graph.Document.Serialize()==expectedCopy.Serialize(),"Duplicate preserves every field, ring, frame, connection and mark");
        Check(Read(duplicate).GetProperty("alreadyApplied").GetBoolean()&&Read(create).GetProperty("isActive").GetBoolean()==false&&window.Graph.Document.Title=="Full copy","Retry does not reopen a background page");
        window.Graph.Selected=["a"];window.SelectionChanged();window.PropositionBox.Text="Copy only";
        var open=Mutation("openGraph");open["documentPath"]=first;open["expectedTargetRevision"]=Read(new{operation="snapshot",documentPath=first}).GetProperty("revision").GetString();
        Read(open);
        Check(window.Graph.Document.Node("a")!.Title==original.Node("a")!.Title&&GraphDocument.Parse(File.ReadAllText(copiedPath)).Node("a")!.Title=="Copy only","Opening saves copy edits and preserves independent original");
        Check(Read(open).GetProperty("alreadyApplied").GetBoolean(),"An identical open retry is idempotent");
        var badOpen=Mutation("openGraph");badOpen["documentPath"]=createdPath;badOpen["expectedTargetRevision"]="stale";Reject(badOpen,"Stale target revision does not switch pages");
        badOpen["documentPath"]=invalidPath;Reject(badOpen,"Invalid target does not switch pages");

        var inactiveCopy=Mutation("duplicateGraph");inactiveCopy["sourceDocument"]=otherPath;inactiveCopy["expectedSourceRevision"]="stale";Reject(inactiveCopy,"Inactive source revision is checked");
        inactiveCopy["expectedSourceRevision"]=Read(new{operation="snapshot",documentPath=otherPath}).GetProperty("revision").GetString();
        var copiedOther=Read(inactiveCopy);Check(GraphDocument.Parse(File.ReadAllText(copiedOther.GetProperty("documentPath").GetString()!)).Node("q")!.Title=="Inactive evidence"&&window.Graph.Document.Title==original.Title,"Inactive source can be copied without switching");

        var occupied=Mutation("createGraph");occupied["title"]="Collision";var occupiedPath=Path.Combine(directory,"agent-"+Guid.Parse(occupied["requestId"]!.GetValue<string>()).ToString("N")+".papergraph");Storage.Save(occupiedPath,new(){Title="Existing content"});
        Reject(occupied,"Creation never replaces an existing destination");
        bool noOverwrite=false;try{Storage.SaveNew(occupiedPath,new(){Title="Wrong"});}catch(IOException){noOverwrite=true;}
        Check(noOverwrite&&GraphDocument.Parse(File.ReadAllText(occupiedPath)).Title=="Existing content","Atomic SaveNew refuses replacement");

        window.Close();window=new MainWindow(directory);
        var countBefore=Directory.GetFiles(directory,"*.papergraph").Length;
        Check(Read(create).GetProperty("alreadyApplied").GetBoolean()&&Read(duplicate).GetProperty("alreadyApplied").GetBoolean()&&Directory.GetFiles(directory,"*.papergraph").Length==countBefore,"Creation receipts prevent duplicate pages after restart");
        Check(window.Graph.Document.Title==original.Title&&GraphDocument.Parse(File.ReadAllText(copiedPath)).Node("a")!.Title=="Copy only","Restart retry preserves later copy edits and active page");
        var returned=GraphDocument.Parse(File.ReadAllText(createdPath));returned.Title="Renamed later";Storage.Save(createdPath,returned);
        Check(Read(create).GetProperty("title").GetString()=="Renamed later","Retry reports current page state without restoring old content");
        File.Move(createdPath,createdPath+".moved");Reject(create,"Retry after a removed page does not recreate it");
        window.Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: agent page listing, inactive read/search, safe paths, pending edits, create/copy/open, exact full graph copy, active/source/target conflicts, gesture guard, independent editing, no overwrite, retry and restart receipts.\n");
    }
}
