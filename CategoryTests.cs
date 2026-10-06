using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
namespace Papergraph;

public static class CategoryTests
{
    public static void Run()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-categories-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"Example.papergraph");Storage.Save(path,new(){Title="Original",Nodes=[new(){Id="n",Title="Research"}]});var original=File.ReadAllBytes(path);
        var window=new MainWindow(directory);
        JsonElement Read(object request)=>JsonSerializer.SerializeToElement(window.HandleAgent(JsonSerializer.SerializeToElement(request)));
        JsonObject Request(string operation)=>new(){["operation"]=operation,["requestId"]=Guid.NewGuid().ToString(),["expectedLibraryRevision"]=Read(new{operation="listGraphs"}).GetProperty("libraryRevision").GetString()};
        var creation=Request("createCategory");creation["title"]="论文分类";var created=Read(creation);var id=created.GetProperty("categoryId").GetString()!;
        Check(((GraphCategory)window.GraphTree.Items[0]).Graphs.Count==0,"Empty categories remain visible");
        Check(Read(creation).GetProperty("alreadyApplied").GetBoolean(),"Category creation retry is idempotent");
        var stale=Request("renameCategory");stale["categoryId"]=id;stale["title"]="Stale";
        var move=Request("moveGraphs");move["categoryId"]=id;move["documentPaths"]=new JsonArray(path);Read(move);
        Check(window.CurrentGraph.CategoryId==id&&((GraphCategory)window.GraphTree.Items[0]).Graphs.Single().Path==path,"Move changes the graph's sidebar parent");
        Check(File.ReadAllBytes(path).SequenceEqual(original)&&window.Graph.Document.Nodes.Single().Title=="Research","Classification does not edit or move the document");
        bool rejected=false;try{Read(stale);}catch{rejected=true;}Check(rejected,"Stale category changes are rejected");
        var invalid=Request("moveGraphs");invalid["categoryId"]=id;invalid["documentPaths"]=new JsonArray(path,Path.Combine(directory,"missing.papergraph"));var revision=Read(new{operation="listGraphs"}).GetProperty("libraryRevision").GetString();rejected=false;try{Read(invalid);}catch{rejected=true;}Check(rejected&&Read(new{operation="listGraphs"}).GetProperty("libraryRevision").GetString()==revision,"Invalid batch does not partially move graphs");
        var rename=Request("renameCategory");rename["categoryId"]=id;rename["title"]="Thesis";Read(rename);
        window.Close();window=new MainWindow(directory);
        Check(window.CurrentGraph.CategoryId==id&&((GraphCategory)window.GraphTree.Items[0]).Title=="Thesis","Categories, titles and membership survive restart");
        Check(Read(creation).GetProperty("alreadyApplied").GetBoolean()&&((GraphCategory)window.GraphTree.Items[0]).Title=="Thesis","A retry after restart does not recreate or rename the category");
        var remove=Request("removeCategory");remove["categoryId"]=id;Read(remove);
        Check(window.CurrentGraph.CategoryId==""&&File.ReadAllBytes(path).SequenceEqual(original)&&window.GraphTree.Items.Count==1,"Removing a category keeps all documents in Unfiled");
        window.Close();window=new MainWindow(directory);Check(window.CurrentGraph.CategoryId=="","Unfiled state persists after category removal");window.Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: empty categories, UI membership, unchanged graph files, agent list/create/rename/move/remove, revision conflicts, atomic batches and persistent retry/restart.\n");
    }
}
