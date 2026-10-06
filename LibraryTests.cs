using System.IO;
using System.Windows;
namespace Papergraph;
public static class LibraryTests
{
    public static void Run()
    {
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-library-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var first=Path.Combine(directory,"Example.papergraph");Storage.Save(first,new(){Title="First graph",Nodes=[new(){Id="a",Title="Original"}]});
        var window=new MainWindow(directory);Check(window.GraphEntries.Count==1,"Existing documents appear in the sidebar");
        window.Graph.Selected=["a"];window.SelectionChanged();window.NotesBox.Text="Unsaved note";
        window.NewGraphButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(window.Graph.Document.Nodes.Count==0&&window.GraphEntries.Count==2,"New graph creates an independent empty document and selects its list entry");
        Check(GraphDocument.Parse(File.ReadAllText(first)).Nodes.Single().Note=="Unsaved note","Creating a graph saves pending content first");
        window.DocumentTitleBox.Text="My second graph";
        var second=window.CurrentGraph;
        Check(second.Title=="My second graph","Editing the title immediately updates the selected row");
        window.SwitchGraph(first);
        Check(window.Graph.Document.Title=="First graph"&&GraphDocument.Parse(File.ReadAllText(second.Path)).Title=="My second graph","Clicking another list item saves the title and opens the chosen graph");
        window.Graph.Selected=["a"];window.SelectionChanged();Check(window.NotesBox.Text=="Unsaved note","Switching restores the original graph content");
        window.SwitchGraph(Path.Combine(directory,"missing.papergraph"));Check(window.Graph.Document.Title=="First graph"&&(window.CurrentGraph).Path==first,"A missing graph leaves current content and selection intact");
        window.Close();window=new MainWindow(directory);Check(window.GraphEntries.Count==2&&window.Graph.Document.Title=="First graph","Restart preserves the list and active graph");
        window.SwitchGraph(second.Path);Check(window.DocumentTitleBox.Text=="My second graph"&&window.Graph.Document.Nodes.Count==0,"Reopening the new graph preserves its independent title and contents");
        var root=(FrameworkElement)window.Content;root.Measure(new Size(1380,880));root.Arrange(new Rect(0,0,1380,880));root.UpdateLayout();Check(window.GraphTree.ActualWidth>120&&window.Graph.ActualWidth>600,"The sidebar reserves space without overlapping the canvas or editor");
        var external=Path.Combine(Path.GetTempPath(),"papergraph-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(external);var exported=Path.Combine(external,"Local graph.papergraph");
        window.DocumentTitleBox.Text="Local saved graph";Check(window.SaveGraphAs(exported),"Save as writes to a chosen local folder");
        Check(GraphDocument.Parse(File.ReadAllText(exported)).Title=="Local saved graph"&&GraphDocument.Parse(File.ReadAllText(second.Path)).Title=="Local saved graph","Save as preserves pending edits in both the original and new file");
        window.Close();window=new MainWindow(directory);Check(window.Graph.Document.Title=="Local saved graph"&&window.GraphEntries.Any(e=>e.Path==exported),"An externally saved graph remains in the list and reopens after restart");
        var trash=Path.Combine(directory,"test-recycle");Directory.CreateDirectory(trash);
        // Substitute only the operating-system recycle operation; all app switching and persistence remain real.
        void Recycle(string path)=>File.Move(path,Path.Combine(trash,Guid.NewGuid().ToString("N")+".papergraph"));
        Check(window.DeleteGraph(exported,Recycle)&&!File.Exists(exported)&&window.GraphEntries.Count==2,"Deleting the active graph removes its file and row, then opens another graph");
        Check(!window.DeleteGraph(second.Path,_=>throw new IOException("Recycle unavailable"))&&File.Exists(second.Path)&&window.GraphEntries.Count==2,"Failed recycling preserves the original file and list entry");
        foreach(var entry in window.GraphEntries.ToArray())Check(window.DeleteGraph(entry.Path,Recycle),"Graph deletion succeeds");
        Check(window.GraphEntries.Count==1&&window.Graph.Document.Nodes.Count==0&&File.Exists((window.CurrentGraph).Path),"Deleting the last graph creates a saved empty graph");
        window.Close();window=new MainWindow(directory);Check(window.GraphEntries.Count==1,"Deleted files do not reappear after restart");window.Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: graph sidebar discovery, live titles, safe switching, restart persistence, layout, local Save as, external documents, deletion, recycle failure and last-graph recovery.\n");
    }
}
