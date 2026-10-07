using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
namespace Papergraph;
public partial class MainWindow
{
    void InitializeLibraryUi()
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(libraryTree,ScrollBarVisibility.Disabled);libraryTree.ItemsSource=categoryRows;
        libraryTree.ItemTemplate=new FuncTreeDataTemplate<LibraryRow>((row,ns)=>{var label=new TextBlock{FontSize=13,FontWeight=row is GraphCategory?FontWeight.SemiBold:FontWeight.Normal,MaxWidth=row is GraphCategory?150:145,TextTrimming=TextTrimming.CharacterEllipsis};label.Bind(TextBlock.TextProperty,new Binding(nameof(LibraryRow.Title)));label.Bind(ToolTip.TipProperty,new Binding(nameof(LibraryRow.Title)));return label;},row=>row is GraphCategory c?c.Graphs:Enumerable.Empty<LibraryRow>());
        var stateStyle=new Style(x=>x.OfType<TreeViewItem>());stateStyle.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty,new Binding(nameof(LibraryRow.IsExpanded)){Mode=BindingMode.TwoWay}));libraryTree.Styles.Add(stateStyle);
        libraryTree.SelectionChanged+=(s,e)=>{if(!libraryUpdating&&libraryTree.SelectedItem is GraphEntry entry)SwitchGraph(entry.Path);};
        libraryTree.DoubleTapped+=(s,e)=>{if(LibraryHit(e.Source) is GraphEntry){documentTitle.Focus();documentTitle.SelectAll();e.Handled=true;}};
        libraryTree.KeyDown+=(s,e)=>{if(e.Key==Key.F2){_=RenameLibraryRow(libraryTree.SelectedItem as LibraryRow);e.Handled=true;}else if(e.Key==Key.Delete&&libraryTree.SelectedItem is GraphEntry entry){SwitchGraph(entry.Path);if(SamePath(file,entry.Path))_=DeleteGraph();e.Handled=true;}};
        libraryTree.AddHandler(PointerPressedEvent,(s,e)=>{if(!e.GetCurrentPoint(libraryTree).Properties.IsRightButtonPressed&&!(OperatingSystem.IsMacOS()&&e.GetCurrentPoint(libraryTree).Properties.IsLeftButtonPressed&&e.KeyModifiers.HasFlag(KeyModifiers.Control)))return;var row=LibraryHit(e.Source);if(row==null)return;ShowLibraryActions(row,libraryTree,PlacementMode.Pointer);e.Handled=true;},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Point dragStart=default;GraphEntry? dragEntry=null;
        libraryTree.AddHandler(PointerPressedEvent,(s,e)=>{if(e.GetCurrentPoint(libraryTree).Properties.IsLeftButtonPressed){dragStart=e.GetPosition(libraryTree);dragEntry=LibraryHit(e.Source) as GraphEntry;}},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        libraryTree.PointerMoved+=async(s,e)=>{if(!e.GetCurrentPoint(libraryTree).Properties.IsLeftButtonPressed){dragEntry=null;return;}if(dragEntry==null||((Vector)(e.GetPosition(libraryTree)-dragStart)).Length<=8)return;var entry=dragEntry;dragEntry=null;var data=new DataTransfer();var item=new DataTransferItem();item.Set(DataFormat.Text,"papergraph-library:"+entry.Path);data.Add(item);await DragDrop.DoDragDropAsync(e,data,DragDropEffects.Move);};
        DragDrop.SetAllowDrop(libraryTree,true);
        libraryTree.AddHandler(DragDrop.DragOverEvent,(s,e)=>{e.DragEffects=LibraryHit(e.Source) is GraphCategory&&e.DataTransfer.TryGetText()?.StartsWith("papergraph-library:")==true?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;});
        libraryTree.AddHandler(DragDrop.DropEvent,(s,e)=>{if(e.DataTransfer.TryGetText() is string text&&text.StartsWith("papergraph-library:")&&LibraryHit(e.Source) is GraphCategory c){var path=text[19..];if(library.Any(x=>SamePath(x.Path,path)))LibraryUi("moveGraphs",new{documentPaths=new[]{path},categoryId=c.Id.Length==0?null:c.Id});}e.Handled=true;});
    }
    void ShowLibraryActions(LibraryRow row,Control target,PlacementMode placement)
    {
        var menu=new ContextMenu();var items=new List<object>();
            if(row is GraphCategory category){items.Add(MenuAction("New graph here",()=>NewGraphInCategory(category.Id)));if(category.Id.Length>0){items.Add(MenuAction("Rename category…",()=>_=RenameLibraryRow(category)));items.Add(MenuAction("Remove category (keep graphs)",()=>LibraryUi("removeCategory",new{categoryId=category.Id})));}}
            else if(row is GraphEntry entry){items.Add(MenuAction("Open",()=>SwitchGraph(entry.Path)));items.Add(MenuAction("Rename",()=>_=RenameLibraryRow(entry)));var move=new MenuItem{Header=T("Move to category")};move.ItemsSource=categoryRows.Select(c=>{var option=MenuAction(c.Title,()=>LibraryUi("moveGraphs",new{documentPaths=new[]{entry.Path},categoryId=c.Id.Length==0?null:c.Id}));option.ToggleType=MenuItemToggleType.CheckBox;option.IsChecked=entry.CategoryId==c.Id;return option;}).ToArray();items.Add(move);items.Add(MenuAction("Save as…",()=>{SwitchGraph(entry.Path);if(SamePath(file,entry.Path))_=SaveAs();}));items.Add(MenuAction("Show in folder",()=>{SwitchGraph(entry.Path);if(SamePath(file,entry.Path))ShowGraphFolder();}));items.Add(MenuAction("Delete graph",()=>{SwitchGraph(entry.Path);if(SamePath(file,entry.Path))_=DeleteGraph();}));}
            menu.ItemsSource=items;menu.Placement=placement;menu.Open(target);
    }
    static LibraryRow? LibraryHit(object? source){var control=source as Visual;return (control as TreeViewItem??control?.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault())?.DataContext as LibraryRow;}
    async Task RenameLibraryRow(LibraryRow? row){if(row is GraphCategory c&&c.Id.Length>0){var title=await Ask("Rename category",c.Title);if(!string.IsNullOrWhiteSpace(title))LibraryUi("renameCategory",new{categoryId=c.Id,title});}else if(row is GraphEntry entry){SwitchGraph(entry.Path);if(SamePath(file,entry.Path)){documentTitle.Focus();documentTitle.SelectAll();}}}
    void NewGraphInCategory(string id){var before=file;NewDocument();if(!SamePath(before,file)&&id.Length>0)LibraryUi("moveGraphs",new{documentPaths=new[]{file},categoryId=id});}
    string treeActivePath="";
    void RefreshLibraryTree(bool reveal=false)
    {
        libraryUpdating=true;try{reveal|=treeActivePath.Length==0||!SamePath(treeActivePath,file);treeActivePath=file;var selectedCategory=reveal?null:(libraryTree.SelectedItem as GraphCategory)?.Id;var expanded=categoryRows.ToDictionary(c=>c.Id,c=>c.IsExpanded);libraryTree.SelectedItem=null;categoryRows.Clear();foreach(var c in catalog.Categories)categoryRows.Add(new(c.Id,c.Name){IsExpanded=expanded.GetValueOrDefault(c.Id,!collapsedCategories.Contains(c.Id))});categoryRows.Add(new("",T("Unfiled")){IsExpanded=expanded.GetValueOrDefault("",!collapsedCategories.Contains(""))});foreach(var entry in library){entry.CategoryId=catalog.Assignments.GetValueOrDefault(entry.Path)??"";entry.IsSelected=selectedCategory==null&&SamePath(entry.Path,file);var category=categoryRows.FirstOrDefault(c=>c.Id==entry.CategoryId)??categoryRows.Last();category.Graphs.Add(entry);if(reveal&&SamePath(entry.Path,file))category.IsExpanded=true;}foreach(var c in categoryRows){c.IsSelected=c.Id==selectedCategory;c.PropertyChanged+=(s,e)=>{if(libraryUpdating||e.PropertyName!=nameof(c.IsExpanded))return;if(c.IsExpanded)collapsedCategories.Remove(c.Id);else collapsedCategories.Add(c.Id);SaveSettings();};}libraryTree.SelectedItem=selectedCategory!=null?categoryRows.FirstOrDefault(c=>c.Id==selectedCategory):library.FirstOrDefault(e=>SamePath(e.Path,file));}finally{libraryUpdating=false;}
    }
}
