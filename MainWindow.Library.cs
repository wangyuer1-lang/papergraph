using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Diagnostics;
namespace Papergraph;

public partial class MainWindow
{
    internal sealed class GraphEntry(string path,string title):INotifyPropertyChanged
    {
        public string Path {get;}=path;
        string title=title;
        public string Title {get=>title;set{title=value;PropertyChanged?.Invoke(this,new(nameof(Title)));}}
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    readonly ObservableCollection<GraphEntry> library=[];
    bool updatingLibrary;
    static bool SamePath(string a,string b)=>string.Equals(System.IO.Path.GetFullPath(a),System.IO.Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
    static string DisplayTitle(string title)=>string.IsNullOrWhiteSpace(title)?"Untitled graph":title;
    void InitializeLibrary()
    {
        var paths=Directory.EnumerateFiles(dataDir).Where(p=>p.EndsWith(".papergraph",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".yujian",StringComparison.OrdinalIgnoreCase)).ToList();
        try{var manifest=Path.Combine(dataDir,"graphs.json");if(File.Exists(manifest))paths.AddRange(JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest))??[]);}catch{}
        foreach(var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try{if(File.Exists(path)&&!SamePath(path,file)){var saved=GraphDocument.Parse(File.ReadAllText(path));library.Add(new(Path.GetFullPath(path),DisplayTitle(saved.Title)));}}catch{}
        }
        library.Insert(0,new(Path.GetFullPath(file),DisplayTitle(doc.Title)));GraphList.ItemsSource=library;GraphList.SelectedItem=library[0];
        NewGraphButton.Click+=(s,e)=>NewDocument();
        GraphList.SelectionChanged+=(s,e)=>{if(!updatingLibrary&&GraphList.SelectedItem is GraphEntry entry)SwitchGraph(entry.Path);};
        GraphList.MouseDoubleClick+=(s,e)=>{DocumentTitleBox.Focus();DocumentTitleBox.SelectAll();e.Handled=true;};
        GraphList.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.F2){DocumentTitleBox.Focus();DocumentTitleBox.SelectAll();e.Handled=true;}else if(e.Key==Key.Delete&&GraphList.SelectedItem is GraphEntry entry){DeleteGraph(entry.Path);e.Handled=true;}};
        GraphList.PreviewMouseRightButtonDown+=(s,e)=>
        {
            if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(GraphList,source) is ListBoxItem row)
            {
                GraphList.SelectedItem=row.DataContext;
                var menu=Menu(GraphList,PlacementMode.MousePoint);
                Item(menu,"Rename",()=>{DocumentTitleBox.Focus();DocumentTitleBox.SelectAll();},"F2");
                Item(menu,"Save as…",SaveCopy);Item(menu,"Show in folder",ShowGraphFolder);
                Item(menu,"Delete graph",()=>DeleteGraph(file),"Del");menu.IsOpen=true;e.Handled=true;
            }
        };
    }
    void UpdateLibraryTitle()
    {
        var entry=library.FirstOrDefault(item=>SamePath(item.Path,file));if(entry!=null)entry.Title=DisplayTitle(doc.Title);
    }
    void TrackCurrentGraph()
    {
        if(GraphList.ItemsSource==null)return;
        updatingLibrary=true;
        try
        {
            var entry=library.FirstOrDefault(item=>SamePath(item.Path,file));
            if(entry==null){entry=new(Path.GetFullPath(file),DisplayTitle(doc.Title));library.Add(entry);}else entry.Title=DisplayTitle(doc.Title);
            GraphList.SelectedItem=entry;
            var json=JsonSerializer.Serialize(library.Select(item=>item.Path).ToArray());var manifest=Path.Combine(dataDir,"graphs.json");
            try{if(!File.Exists(manifest)||File.ReadAllText(manifest)!=json){var pending=manifest+".tmp";File.WriteAllText(pending,json);File.Move(pending,manifest,true);}}catch{}
        }
        finally{updatingLibrary=false;}
    }
    internal void SwitchGraph(string path)
    {
        if(SamePath(path,file))return;
        if(!SaveBeforeSwitch()){TrackCurrentGraph();return;}
        try{var loaded=GraphDocument.Parse(File.ReadAllText(path));ResetDocument(loaded,path,false);}
        catch(Exception ex){TrackCurrentGraph();Notify("Could not open this graph: "+ex.Message);}
    }
    void ShowGraphFolder()
    {
        if(!Save())return;
        try{Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{Path.GetFullPath(file)}\""){UseShellExecute=true});}
        catch(Exception ex){Notify("Could not open the folder: "+ex.Message);}
    }
    internal bool SaveGraphAs(string destination)
    {
        if(!SaveBeforeSwitch())return false;
        try{Storage.Save(destination,doc);file=Path.GetFullPath(destination);dirty=false;revision++;WriteRecent();SaveDot.ToolTip="Saved to "+file;Notify("Saved to "+file);return true;}
        catch(Exception ex){Notify("Could not save this graph: "+ex.Message);return false;}
    }
    internal bool DeleteGraph(string path,Action<string>? recycle=null)
    {
        var entry=library.FirstOrDefault(item=>SamePath(item.Path,path));if(entry==null)return false;
        if(!SaveBeforeSwitch())return false;
        if(SamePath(path,file))
        {
            var next=library.FirstOrDefault(item=>!SamePath(item.Path,path)&&File.Exists(item.Path));
            if(next!=null)SwitchGraph(next.Path);else NewDocument();
            if(SamePath(path,file)||dirty)return false;
        }
        try
        {
            if(File.Exists(path))
            {
                if(recycle!=null)recycle(path);
                else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            }
            updatingLibrary=true;try{library.Remove(entry);}finally{updatingLibrary=false;}
            TrackCurrentGraph();Notify("Graph moved to the Recycle Bin");return true;
        }
        catch(Exception ex){TrackCurrentGraph();Notify("Could not delete this graph: "+ex.Message);return false;}
    }
}
