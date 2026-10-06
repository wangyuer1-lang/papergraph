using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Diagnostics;
namespace Papergraph;

public partial class MainWindow
{
    readonly ObservableCollection<GraphEntry> library=[];
    readonly ObservableCollection<GraphCategory> categoryRows=[];
    GraphLibraryCatalog catalog=new();
    bool updatingLibrary;
    string treeActivePath="";
    readonly HashSet<string> collapsedCategories=[];
    internal IReadOnlyList<GraphEntry> GraphEntries=>library;
    internal GraphEntry CurrentGraph=>library.First(e=>SamePath(e.Path,file));
    string CatalogPath=>Path.Combine(dataDir,"graph-categories.json");
    static bool SamePath(string a,string b)=>string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
    static string DisplayTitle(string title)=>string.IsNullOrWhiteSpace(title)?"Untitled graph":title;
    void InitializeLibrary()
    {
        var paths=Directory.EnumerateFiles(dataDir).Where(p=>p.EndsWith(".papergraph",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".yujian",StringComparison.OrdinalIgnoreCase)).ToList();
        try{var manifest=Path.Combine(dataDir,"graphs.json");if(File.Exists(manifest))paths.AddRange(JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest))??[]);}catch{}
        foreach(var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try{if(File.Exists(path)&&!SamePath(path,file)){var saved=GraphDocument.Parse(File.ReadAllText(path));library.Add(new(Path.GetFullPath(path),DisplayTitle(saved.Title)));}}catch{}
        }
        library.Insert(0,new(Path.GetFullPath(file),DisplayTitle(doc.Title)));
        try{if(File.Exists(CatalogPath))catalog=GraphLibraryCatalog.Parse(File.ReadAllText(CatalogPath));}
        catch(Exception ex){Notify(Localization.Text("Could not load categories; original file kept: ")+ex.Message);}
        GraphTree.ItemsSource=categoryRows;RefreshLibraryTree(true);
        NewGraphButton.Click+=(s,e)=>NewGraphInCategory((GraphTree.SelectedItem as GraphCategory)?.Id??CurrentGraph.CategoryId);
        NewCategoryButton.Click+=(s,e)=>{var name=Ask(Localization.Text("New category"),"");if(name!=null)LibraryUi("createCategory",new{title=name});};
        GraphTree.SelectedItemChanged+=(s,e)=>{if(!updatingLibrary&&e.NewValue is GraphEntry entry)SwitchGraph(entry.Path);};
        GraphTree.MouseDoubleClick+=(s,e)=>{if(LibraryHit(e.OriginalSource) is GraphEntry){DocumentTitleBox.Focus();DocumentTitleBox.SelectAll();e.Handled=true;}};
        GraphTree.PreviewKeyDown+=(s,e)=>
        {
            if(e.Key==Key.F2){RenameLibraryRow(GraphTree.SelectedItem);e.Handled=true;}
            else if(e.Key==Key.Delete&&GraphTree.SelectedItem is GraphEntry entry){DeleteGraph(entry.Path);e.Handled=true;}
        };
        GraphTree.PreviewMouseRightButtonDown+=(s,e)=>
        {
            var row=LibraryHit(e.OriginalSource);if(row==null)return;
            var menu=Menu(GraphTree,PlacementMode.MousePoint);
            if(row is GraphCategory category)
            {
                LocalizedItem(menu,"New graph here",()=>NewGraphInCategory(category.Id));
                if(category.Id.Length>0){LocalizedItem(menu,"Rename category…",()=>RenameLibraryRow(category));LocalizedItem(menu,"Remove category (keep graphs)",()=>LibraryUi("removeCategory",new{categoryId=category.Id}));}
            }
            else if(row is GraphEntry entry)
            {
                LocalizedItem(menu,"Open",()=>SwitchGraph(entry.Path));LocalizedItem(menu,"Rename",()=>RenameLibraryRow(entry),"F2");
                var move=new MenuItem{Header=Localization.Text("Move to category")};
                foreach(var c in categoryRows){var id=c.Id;var option=Item(move,c.Title,()=>LibraryUi("moveGraphs",new{documentPaths=new[]{entry.Path},categoryId=id.Length==0?null:id}));option.IsCheckable=true;option.IsChecked=entry.CategoryId==id;}menu.Items.Add(move);
                LocalizedItem(menu,"Save as…",()=>{SwitchGraph(entry.Path);if(SamePath(file,entry.Path))SaveCopy();});
                LocalizedItem(menu,"Show in folder",()=>{SwitchGraph(entry.Path);if(SamePath(file,entry.Path))ShowGraphFolder();});
                LocalizedItem(menu,"Delete graph",()=>DeleteGraph(entry.Path),"Del");
            }
            menu.IsOpen=true;e.Handled=true;
        };
        Point dragStart=default;GraphEntry? dragEntry=null;
        GraphTree.PreviewMouseLeftButtonDown+=(s,e)=>{dragStart=e.GetPosition(GraphTree);dragEntry=LibraryHit(e.OriginalSource) as GraphEntry;};
        GraphTree.PreviewMouseMove+=(s,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&dragEntry!=null&&(e.GetPosition(GraphTree)-dragStart).Length>8){var entry=dragEntry;dragEntry=null;DragDrop.DoDragDrop(GraphTree,new DataObject("Papergraph.LibraryGraph",entry.Path),DragDropEffects.Move);}};
        GraphTree.DragOver+=(s,e)=>{e.Effects=e.Data.GetDataPresent("Papergraph.LibraryGraph")&&LibraryHit(e.OriginalSource) is GraphCategory?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;};
        GraphTree.Drop+=(s,e)=>{if(e.Data.GetData("Papergraph.LibraryGraph") is string path&&LibraryHit(e.OriginalSource) is GraphCategory c)LibraryUi("moveGraphs",new{documentPaths=new[]{path},categoryId=c.Id.Length==0?null:c.Id});e.Handled=true;};
    }
    static LibraryRow? LibraryHit(object source)
    {
        var element=source as DependencyObject;
        while(element!=null){if(element is TreeViewItem item)return item.DataContext as LibraryRow;element=element is Visual?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);}return null;
    }
    void RenameLibraryRow(object? row)
    {
        if(row is GraphCategory c&&c.Id.Length>0){var name=Ask(Localization.Text("Rename category"),c.Title);if(name!=null)LibraryUi("renameCategory",new{categoryId=c.Id,title=name});}
        else if(row is GraphEntry e){SwitchGraph(e.Path);if(SamePath(file,e.Path)){DocumentTitleBox.Focus();DocumentTitleBox.SelectAll();}}
    }
    void LibraryUi(string operation,object fields)
    {
        try{var request=JsonSerializer.SerializeToNode(fields)!.AsObject();request["operation"]=operation;request["requestId"]=Guid.NewGuid().ToString();request["expectedLibraryRevision"]=catalog.Revision();AgentManageCategories(JsonSerializer.SerializeToElement(request),operation);}
        catch(Exception ex){Notify(Localization.Text("Could not update categories: ")+ex.Message);}
    }
    void NewGraphInCategory(string id)
    {
        var before=file;NewDocument();if(!SamePath(before,file)&&id.Length>0)LibraryUi("moveGraphs",new{documentPaths=new[]{Path.GetFullPath(file)},categoryId=id});
    }
    void RefreshLibraryTree(bool reveal=false)
    {
        updatingLibrary=true;
        try
        {
            reveal|=treeActivePath.Length==0||!SamePath(treeActivePath,file);treeActivePath=file;
            var selectedCategory=reveal?null:(GraphTree.SelectedItem as GraphCategory)?.Id;
            var expanded=categoryRows.ToDictionary(c=>c.Id,c=>c.IsExpanded);
            categoryRows.Clear();
            foreach(var c in catalog.Categories)categoryRows.Add(new(c.Id,c.Name){IsExpanded=expanded.GetValueOrDefault(c.Id,!collapsedCategories.Contains(c.Id))});
            categoryRows.Add(new("",Localization.Text("Unfiled")){IsExpanded=expanded.GetValueOrDefault("",!collapsedCategories.Contains(""))});
            foreach(var entry in library){entry.CategoryId=catalog.Assignments.GetValueOrDefault(entry.Path,"");entry.IsSelected=false;var category=categoryRows.FirstOrDefault(c=>c.Id==entry.CategoryId)??categoryRows.Last();category.Graphs.Add(entry);if(SamePath(entry.Path,file)){if(reveal)category.IsExpanded=true;entry.IsSelected=selectedCategory==null;}}
            foreach(var category in categoryRows)
            {
                category.IsSelected=category.Id==selectedCategory;
                category.PropertyChanged+=(s,e)=>{if(!updatingLibrary&&e.PropertyName==nameof(GraphCategory.IsExpanded)){if(category.IsExpanded)collapsedCategories.Remove(category.Id);else collapsedCategories.Add(category.Id);SaveViewSettings();}};
            }
        }
        finally{updatingLibrary=false;}
    }
    void UpdateLibraryTitle()
    {
        var entry=library.FirstOrDefault(item=>SamePath(item.Path,file));if(entry!=null)entry.Title=DisplayTitle(doc.Title);
    }
    void TrackCurrentGraph()
    {
        if(GraphTree.ItemsSource==null)return;
        updatingLibrary=true;
        try
        {
            var entry=library.FirstOrDefault(item=>SamePath(item.Path,file));
            if(entry==null){entry=new(Path.GetFullPath(file),DisplayTitle(doc.Title));library.Add(entry);}else entry.Title=DisplayTitle(doc.Title);
            var json=JsonSerializer.Serialize(library.Select(item=>item.Path).ToArray());var manifest=Path.Combine(dataDir,"graphs.json");
            try{if(!File.Exists(manifest)||File.ReadAllText(manifest)!=json){var pending=manifest+".tmp";File.WriteAllText(pending,json);File.Move(pending,manifest,true);}}catch{}
        }
        finally{updatingLibrary=false;}
        RefreshLibraryTree();
    }
    internal void SwitchGraph(string path)
    {
        if(SamePath(path,file))return;
        if(!SaveBeforeSwitch()){TrackCurrentGraph();return;}
        try{var loaded=GraphDocument.Parse(File.ReadAllText(path));ResetDocument(loaded,path,false);}
        catch(Exception ex){TrackCurrentGraph();Notify(Localization.Text("Could not open this graph: ")+ex.Message);}
    }
    void ShowGraphFolder()
    {
        if(!Save())return;
        try{Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{Path.GetFullPath(file)}\""){UseShellExecute=true});}
        catch(Exception ex){Notify(Localization.Text("Could not open the folder: ")+ex.Message);}
    }
    internal bool SaveGraphAs(string destination)
    {
        if(!SaveBeforeSwitch())return false;
        try{Storage.Save(destination,doc);file=Path.GetFullPath(destination);dirty=false;revision++;WriteRecent();SaveDot.ToolTip=Localization.Text("Saved to ")+file;Notify(Localization.Text("Saved to ")+file);return true;}
        catch(Exception ex){Notify(Localization.Text("Could not save this graph: ")+ex.Message);return false;}
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
            TrackCurrentGraph();Notify(Localization.Text("Graph moved to the Recycle Bin"));return true;
        }
        catch(Exception ex){TrackCurrentGraph();Notify(Localization.Text("Could not delete this graph: ")+ex.Message);return false;}
    }
}
