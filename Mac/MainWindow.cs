using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.ObjectModel;
using System.Text.Json;
using P=System.Windows.Point;
using R=System.Windows.Rect;
using V=System.Windows.Vector;
namespace Papergraph;

public partial class MainWindow:Window
{
    readonly string dataDir;
    string file="",editorKind="",editKey="";string? editorId;
    GraphDocument doc=new();bool dirty,updating,dark,libraryUpdating,ensurePending;long revision;DateTime editAt;
    readonly Stack<(string Json,string? Scope,string? Region)> undo=[],redo=[];
    sealed record BoardState(string? Scope,string? Region,double Zoom,P Offset,HashSet<string> Selected,string? Edge,HashSet<string> Regions,R? Box,(double Zoom,P Offset)? Detail);
    readonly Stack<BoardState> boards=[];
    readonly ObservableCollection<GraphEntry> library=[];
    GraphLibraryCatalog catalog=new();
    string CatalogPath=>Path.Combine(dataDir,"graph-categories.json");
    readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(650)};
    readonly DispatcherTimer textTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    internal readonly GraphSurface Graph=new();
    readonly TreeView libraryTree=new();
    readonly ObservableCollection<GraphCategory> categoryRows=[];
    readonly HashSet<string> collapsedCategories=[];
    readonly Dictionary<string,int> noteSelections=[];
    readonly TextBox documentTitle=new(){FontSize=18,FontWeight=FontWeight.SemiBold,BorderThickness=new Thickness(0),MinWidth=160};
    readonly TextBox captionBox=new(){Watermark="Short title",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};
    readonly TextBox bodyBox=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=180};
    readonly TextBox notesBox=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=180};

    readonly TabStrip notePages=new(){ItemsSource=new[]{"1","2","3","4"},SelectedIndex=0};
    readonly TextBlock heading=new(){FontSize=20,FontWeight=FontWeight.SemiBold};
    readonly TextBlock status=new(){Text="Ready",VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
    readonly TextBlock zoomLabel=new(){VerticalAlignment=VerticalAlignment.Center};

    readonly StackPanel edgePanel=new(){Spacing=8};
    readonly TabStrip textPages=new();
    readonly TextBlock textStats=new(){TextWrapping=TextWrapping.Wrap};

    readonly Grid graphArea=new(),textArea=new();
    bool fullTextVisible,buildingText;int fullTextPage;
    readonly List<Action> localize=[];bool collectLabels=true;
    internal sealed class Indicator(MainWindow owner)
    {
        System.Windows.Media.Brush? fill;
        public System.Windows.Media.Brush? Fill{get=>fill;set{fill=value;if(value!=null)owner.saveIndicator.Fill=value.Native;}}
        public string ToolTip{get=>Avalonia.Controls.ToolTip.GetTip(owner.saveIndicator) as string??"";set=>Avalonia.Controls.ToolTip.SetTip(owner.saveIndicator,value);}
    }
    readonly Indicator SaveDot;
    internal GraphEntry CurrentGraph=>library.First(e=>SamePath(e.Path,file));
    internal TextBox NotesBox=>notesBox;internal TextBox PropositionBox=>bodyBox;
    internal GraphDocument Document=>doc;
    internal string DocumentPath=>file;
    internal MainWindow(string directory)
    {
        dataDir=directory;SaveDot=new(this);Directory.CreateDirectory(directory);
        Width=1380;Height=880;MinWidth=800;MinHeight=540;Title="papergraph";Icon=MacApplicationIcon.WindowIcon();WindowStartupLocation=WindowStartupLocation.CenterScreen;
        LoadSettings();LoadInitial();dirty|=GraphGroups.ExpandLegacy(doc);dirty|=doc.MaterializeRegions();LoadLibrary();InitializePalette();BuildUi();ApplyTheme();InitializeInteraction();
        Graph.Document=doc;
        Graph.SelectionChanged+=SelectionChanged;Graph.BeforeChange+=Remember;Graph.Changed+=()=>Changed();
        Graph.LayoutFinished+=()=>Changed(false);Graph.LayoutFailed+=m=>Notify(m);Graph.LayoutNotice+=m=>Notify(m);
        Graph.CreateNode+=AddNode;Graph.CreateRegion+=AddRegion;Graph.Connect+=AddEdge;
        Graph.EnterCircle+=id=>EnterBoard(id,null);Graph.EnterRegion+=id=>EnterBoard(doc.Frame(id)?.Parent,id);
        Graph.EditRequested+=()=>bodyBox.Focus();Graph.ContextRequested+=ShowGraphContext;
        Graph.BoxSelectionCompleted+=box=>{ensurePending=false;Graph.Focus();};
        Graph.ViewChanged+=()=>zoomLabel.Text=$"{Graph.Zoom*100:0}%";
        documentTitle.PropertyChanged+=(s,e)=>{if(e.Property!=TextBox.TextProperty)return;if(updating)return;RememberEdit("document");doc.Title=documentTitle.Text??"";UpdateLibraryTitle();Changed(false);};
        captionBox.PropertyChanged+=(s,e)=>{if(e.Property==TextBox.TextProperty)EditSelected("caption",captionBox.Text??"");};
        bodyBox.PropertyChanged+=(s,e)=>{if(e.Property==TextBox.TextProperty)EditSelected("body",bodyBox.Text??"");};
        notePages.SelectionChanged+=(s,e)=>{if(!updating&&editorId!=null){noteSelections[NoteSelectionKey]=Math.Max(0,notePages.SelectedIndex);editKey="";RefreshNotes();}};
        notesBox.PropertyChanged+=(s,e)=>{if(e.Property==TextBox.TextProperty)EditNote(false);};
        saveTimer.Tick+=(s,e)=>{saveTimer.Stop();Save();};textTimer.Tick+=(s,e)=>{textTimer.Stop();RefreshFullText();};
        textPages.SelectionChanged+=(s,e)=>{if(!buildingText){SelectFullTextPage(Math.Max(0,textPages.SelectedIndex));}};
        AddHandler(KeyDownEvent,Keys,Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closing+=(s,e)=>{if(!Save())e.Cancel=true;else{saveTimer.Stop();textTimer.Stop();toastTimer.Stop();SaveSettings();}};
        Opened+=(s,e)=>{MacApplicationIcon.Apply();RefreshAll();Graph.Fit(false);Graph.Focus();if(dirty)Save();};
        RefreshAll();
    }
    string T(string text)=>Localization.Text(text).Replace("Ctrl+Y","⌘⇧Z").Replace("Ctrl+","⌘").Replace("Ctrl ","⌘ ");
    Button Button(string text,Action action)
    {
        var button=new Button{Content=T(text),Padding=new Thickness(10,7)};
        if(collectLabels&&text.Length>0)localize.Add(()=>button.Content=T(text));button.Click+=(s,e)=>{Safe(action);if(ReferenceEquals(FocusManager?.GetFocusedElement(),button))Graph.Focus();};return button;
    }
    MenuItem MenuAction(string text,Action action,string? shortcut=null)
    {
        var item=new MenuItem{Header=T(text)};if(shortcut!=null)item.InputGesture=KeyGesture.Parse(shortcut);
        if(collectLabels)localize.Add(()=>item.Header=T(text));item.Click+=(s,e)=>Safe(action);return item;
    }
    TextBlock Label(string source){var label=new TextBlock{Text=T(source),FontWeight=FontWeight.SemiBold};localize.Add(()=>label.Text=T(source));return label;}
    void Safe(Action action){try{action();}catch(Exception ex){Notify(ex.Message);}}
    void LoadSettings()
    {
        try{using var j=JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir,"mac-settings.json")));var root=j.RootElement;dark=root.GetProperty("dark").GetBoolean();Localization.Language=Localization.Normalize(root.GetProperty("language").GetString());if(root.TryGetProperty("fullTextVisible",out var full))fullTextVisible=full.GetBoolean();if(root.TryGetProperty("connectionDisplay",out var display)&&Enum.TryParse<ConnectionDisplay>(display.GetString(),out var mode))Graph.ConnectionDisplay=mode;if(root.TryGetProperty("collapsedCategories",out var categories))foreach(var c in categories.EnumerateArray())collapsedCategories.Add(c.GetString()!);}catch{}
    }
    void SaveSettings(){try{File.WriteAllText(Path.Combine(dataDir,"mac-settings.json"),JsonSerializer.Serialize(new{dark,language=Localization.Language,fullTextVisible,connectionDisplay=Graph.ConnectionDisplay.ToString(),collapsedCategories}));}catch(Exception ex){Notify(ex.Message);}}
    void ApplyTheme()
    {
        RequestedThemeVariant=dark?Avalonia.Styling.ThemeVariant.Dark:Avalonia.Styling.ThemeVariant.Light;
        InitializePalette();
        foreach(var refresh in themeBindings)refresh();Graph.ApplyTheme(dark);((ToolbarGlyph)themeButton.Content!).Symbol=dark?"☀":"☾";Tip(themeButton,dark?"Switch to light theme":"Switch to dark theme");themeButton.InvalidateVisual();((ToolbarGlyph)themeButton.Content!).InvalidateVisual();SaveDot.Fill=GraphStyle.Brush(dirty?(dark?"#9EABBC":"#7C8997"):dark?"#A6BCAF":"#789989");SaveDot.ToolTip=T(dirty?"Saving":"Saved locally");SelectionChanged();
    }
    void InitializePalette()
    {
        var keys=new[]{"CanvasBrush","SurfaceBrush","TextBrush","MutedBrush","BorderBrush","HoverBrush","AccentBrush"};var colors=dark?new[]{"#191C22","#232730","#E1E6ED","#9EABBC","#363E4B","#313A49","#9CC2FF"}:new[]{"#F7F8FA","#FFFFFF","#273747","#7C8997","#DFE5EC","#E9EEF5","#356FBD"};for(int i=0;i<keys.Length;i++)Resources[keys[i]]=new SolidColorBrush(Color.Parse(colors[i]));
        foreach(var key in new[]{"TreeViewItemForeground","TreeViewItemForegroundSelected","TreeViewItemForegroundPointerOver","MenuFlyoutItemForeground","MenuFlyoutItemForegroundPointerOver","MenuFlyoutItemForegroundPressed","MenuFlyoutSubItemChevron"})Resources[key]=Ui("TextBrush");
        foreach(var key in new[]{"TreeViewItemBackgroundSelected","TreeViewItemBackgroundSelectedPointerOver","TreeViewItemBackgroundPointerOver","MenuFlyoutItemBackgroundPointerOver","MenuFlyoutItemBackgroundPressed"})Resources[key]=Ui("HoverBrush");
        Resources["MenuFlyoutPresenterBackground"]=Ui("SurfaceBrush");Resources["MenuFlyoutPresenterBorderBrush"]=Ui("BorderBrush");Resources["MenuFlyoutItemKeyboardAcceleratorTextForeground"]=Ui("MutedBrush");Resources["MenuFlyoutPresenterThemePadding"]=new Thickness(5);Resources["OverlayCornerRadius"]=new CornerRadius(6);

    }
    void LoadInitial()
    {
        var recent=Path.Combine(dataDir,"recent.txt");
        if(File.Exists(recent))try{var path=File.ReadAllText(recent).Trim();if(!Path.IsPathRooted(path))path=Path.Combine(dataDir,path);if(File.Exists(path)){doc=GraphDocument.Parse(File.ReadAllText(path));file=Path.GetFullPath(path);return;}}catch(Exception ex){Notify("Could not read previous graph: "+ex.Message);}
        var example=Path.Combine(dataDir,"Example.papergraph");
        if(File.Exists(example)){doc=GraphDocument.Parse(File.ReadAllText(example));file=example;return;}
        doc=GraphDocument.Demo();file=FreshPath("Example");dirty=true;
    }
    string FreshPath(string title)
    {
        var safe=string.Concat(title.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim();if(safe.Length==0)safe="Untitled";if(safe.Length>55)safe=safe[..55];
        var path=Path.Combine(dataDir,safe+".papergraph");return File.Exists(path)?Path.Combine(dataDir,safe+"-"+Guid.NewGuid().ToString("N")[..8]+".papergraph"):path;
    }
    static bool SamePath(string a,string b)=>string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
    static string DisplayTitle(string title)=>string.IsNullOrWhiteSpace(title)?"Untitled graph":title;
    void LoadLibrary()
    {
        var paths=Directory.EnumerateFiles(dataDir).Where(p=>p.EndsWith(".papergraph",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".yujian",StringComparison.OrdinalIgnoreCase)).ToList();
        try{paths.AddRange(JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(dataDir,"graphs.json")))??[]);}catch{}
        foreach(var path in paths.Distinct())try{if(File.Exists(path)&&!SamePath(path,file)){var saved=GraphDocument.Parse(File.ReadAllText(path));library.Add(new(Path.GetFullPath(path),DisplayTitle(saved.Title)));}}catch{}
        library.Insert(0,new(file,DisplayTitle(doc.Title)));
        try{if(File.Exists(CatalogPath))catalog=GraphLibraryCatalog.Parse(File.ReadAllText(CatalogPath));}catch(Exception ex){Notify("Could not load categories: "+ex.Message);}
    }
    void TrackCurrentGraph()
    {
        var entry=library.FirstOrDefault(e=>SamePath(e.Path,file));if(entry==null)library.Add(new(file,DisplayTitle(doc.Title)));else entry.Title=DisplayTitle(doc.Title);
        File.WriteAllText(Path.Combine(dataDir,"graphs.json"),JsonSerializer.Serialize(library.Select(e=>e.Path).ToArray()));RefreshLibraryTree();
    }
    void UpdateLibraryTitle(){var entry=library.FirstOrDefault(e=>SamePath(e.Path,file));if(entry!=null)entry.Title=DisplayTitle(doc.Title);Title=DisplayTitle(doc.Title)+" — papergraph";}
    void Remember()=>RecordUndo(true);
    void RecordUndo(bool cancelLayout){if(cancelLayout)Graph.CancelLayout();undo.Push((doc.Serialize(),Graph.Scope,Graph.BoardRegion));redo.Clear();editKey="";}
    void RememberEdit(string key){if(editKey!=key||(DateTime.UtcNow-editAt).TotalSeconds>2){RecordUndo(false);editKey=key;}editAt=DateTime.UtcNow;}
    void Changed(bool structure=true)
    {
        dirty=true;revision++;saveTimer.Stop();saveTimer.Start();SaveDot.Fill=GraphStyle.Brush(dark?"#9EABBC":"#7C8997");SaveDot.ToolTip=T("Saving");
        if(structure){Graph.RefreshData();SelectionChanged();}else Graph.ContentChanged();textTimer.Stop();textTimer.Start();
    }
    bool Save()
    {
        if(!dirty)return true;
        try{Storage.Save(file,doc);dirty=false;WriteRecent();SaveDot.Fill=GraphStyle.Brush(dark?"#A6BCAF":"#789989");SaveDot.ToolTip=T("Saved to ")+file;return true;}
        catch(Exception ex){SaveDot.Fill=GraphStyle.Brush("#D07974");SaveDot.ToolTip=T("Not saved · Ctrl+S to retry");Notify("Save failed: "+ex.Message);return false;}
    }
    void WriteRecent(){TrackCurrentGraph();File.WriteAllText(Path.Combine(dataDir,"recent.txt"),file);}
    void Notify(string text,bool canUndo=false){status.Text=T(text).Replace("Ctrl+","⌘");toast.IsVisible=true;toastUndo.IsVisible=canUndo;toastTimer.Stop();toastTimer.Start();}
    void RefreshAll(){updating=true;documentTitle.Text=doc.Title;updating=false;Graph.Document=doc;UpdateLibraryTitle();SelectionChanged();RefreshLibraryTree();RefreshFullText(true);}
    void Undo(){if(undo.Count==0)return;redo.Push((doc.Serialize(),Graph.Scope,Graph.BoardRegion));Restore(undo.Pop());}
    void Redo(){if(redo.Count==0)return;undo.Push((doc.Serialize(),Graph.Scope,Graph.BoardRegion));Restore(redo.Pop());}
    void Restore((string Json,string? Scope,string? Region) state){doc=GraphDocument.Parse(state.Json);Graph.Document=doc;Graph.SetBoard(state.Scope,state.Region);Graph.ClearAllSelection();editorId=null;editKey="";RefreshAll();Changed();}
    void ResetDocument(GraphDocument next,string path,bool modified)
    {
        saveTimer.Stop();doc=next;file=Path.GetFullPath(path);dirty=modified;dirty|=GraphGroups.ExpandLegacy(doc);dirty|=doc.MaterializeRegions();undo.Clear();redo.Clear();boards.Clear();Graph.ClearAllSelection();Graph.SetBoard(null,null);editorId=null;fullTextPage=0;RefreshAll();TrackCurrentGraph();Graph.Fit(false);if(dirty)saveTimer.Start();WriteRecent();
    }
    bool SaveBeforeSwitch()=>Save();
    void SwitchGraph(string path){if(SamePath(path,file))return;if(!SaveBeforeSwitch())return;Safe(()=>ResetDocument(GraphDocument.Parse(File.ReadAllText(path)),path,false));}
    internal void OpenPath(string path){if(!SaveBeforeSwitch())return;Safe(()=>ResetDocument(GraphDocument.Parse(File.ReadAllText(path)),path,false));}
    void NewDocument(){if(!SaveBeforeSwitch())return;var next=new GraphDocument{Title=T("Untitled paper")};ResetDocument(next,FreshPath(next.Title),true);Save();}
    void DuplicateGraph(){if(!Save())return;var next=GraphDocument.Parse(doc.Serialize());next.Title+=" (copy)";ResetDocument(next,FreshPath(next.Title),true);Save();}
    IGraphNotes? SelectedObject()=>editorKind switch{"node"=>doc.Node(editorId),"region"=>doc.Frame(editorId),"edge"=>doc.Edges.FirstOrDefault(e=>e.Id==editorId),_=>null};
    internal void SelectionChanged()
    {
        updating=true;var previousId=editorId;var previousKind=editorKind;editorId=null;editorKind="";
        Graph.Selected.RemoveWhere(id=>!Graph.VisibleNodes.Any(n=>n.Id==id));Graph.SelectedRegions.RemoveWhere(id=>!Graph.VisibleRegions.Any(r=>r.Id==id));
        if(Graph.Selected.Count==1&&Graph.SelectionCount==1){editorId=Graph.Selected.First();editorKind="node";}
        else if(Graph.SelectedRegion!=null&&Graph.SelectionCount==1){editorId=Graph.SelectedRegion;editorKind="region";}
        else if(Graph.SelectionCount==0&&Graph.SelectedEdge!=null){editorId=Graph.SelectedEdge;editorKind="edge";}
        var item=SelectedObject();bool open=item!=null;emptyEditor.IsVisible=!open;notesPanel.IsVisible=moreButton.IsVisible=captionPanel.IsVisible=open;
        // Hide the entire relation viewport so its empty presenter cannot
        // intercept clicks on the proposition editor beneath it.
        bodyBox.IsVisible=open&&editorKind!="edge";edgeScroll.IsVisible=edgePanel.IsVisible=editorKind=="edge";linkButton.IsVisible=open&&editorKind!="edge";colorButton.IsVisible=editorKind is "node" or "edge";editorSymbol.IsVisible=editorKind=="region";
        heading.Text=item is Proposition {Kind:"circle"}?"":T(editorKind switch{"node"=>"Proposition","edge"=>"Relation","region"=>"Frame",_=>""});
        {SetEditorText(captionBox,item switch{Proposition n=>n.Caption,Relation e=>e.Caption,Region r=>r.Caption,_=>""});SetEditorText(bodyBox,item switch{Proposition n=>n.Title,Region r=>r.Title,_=>""});}
        if(item is Relation edge){nodeGlyph.Kind="point";nodeGlyph.Stroke=new SolidColorBrush(GraphMarkColors.Edge(edge,dark));BuildArrowChoices(edge);}
        if(item is Proposition node){nodeGlyph.Kind=node.Kind;nodeGlyph.Stroke=new SolidColorBrush(GraphMarkColors.Node(doc,node,dark));}nodeGlyph.InvalidateVisual();
        multiBar.IsVisible=Graph.GroupSelection;frameButton.IsEnabled=FrameCreationBounds()!=null;circleButton.IsEnabled=GraphGroups.CanGroup(doc,Graph.Selected);deleteButton.IsEnabled=Graph.SelectionCount>0;
        backButton.IsVisible=Graph.Scope!=null||Graph.BoardRegion!=null;Graph.RightInset=0;RefreshNotes();updating=false;Graph.RefreshSelection();HighlightFullText();UpdateMacControls();if(editorKind=="node"&&previousId!=editorId){ensurePending=true;Dispatcher.UIThread.Post(EnsureSelectedVisible,DispatcherPriority.Loaded);}
    }
    void EnsureSelectedVisible(){if(!ensurePending||Graph.IsInteracting)return;ensurePending=false;if(editorKind=="node"&&editorId!=null)Graph.EnsureVisible(editorId);}
    static void SetEditorText(TextBox box,string text){if(box.Text==text)return;box.IsUndoEnabled=false;box.Text=text;box.IsUndoEnabled=true;}
    void EditSelected(string field,string value)
    {
        if(updating||editorId==null)return;var item=SelectedObject();if(item==null)return;RememberEdit(field+editorId);
        switch(item){case Proposition n:if(field=="caption")n.Caption=value;else n.Title=value;break;case Region r:if(field=="caption")r.Caption=value;else r.Title=value;break;case Relation e:if(field=="caption")e.Caption=value;break;}
        Changed(false);
    }
    string NoteSelectionKey=>file+"|"+editorKind+"|"+editorId;
    void RefreshNotes(){var was=updating;updating=true;var item=SelectedObject();int page=Math.Clamp(noteSelections.GetValueOrDefault(NoteSelectionKey),0,3);notePages.SelectedIndex=page;SetEditorText(notesBox,item==null?"":page==0?item.Note:item.AdditionalNotes[page-1].Body);updating=was;}
    void EditNote(bool title){if(updating||SelectedObject() is not IGraphNotes item)return;RememberEdit("note"+editorId+notePages.SelectedIndex);var page=Math.Max(0,notePages.SelectedIndex);if(page==0)item.Note=notesBox.Text??"";else item.AdditionalNotes[page-1].Body=notesBox.Text??"";Changed(false);}
    void AddNode(P center){Remember();center=Graph.ClampToBoard(center);var n=new Proposition{Parent=Graph.Scope,X=center.X-125,Y=center.Y-60};doc.Nodes.Add(n);Graph.ClearAllSelection();Graph.Selected=[n.Id];CancelLink();Changed();ensurePending=false;Graph.StartRelaxation([n.Id]);bodyBox.Focus();}
}
