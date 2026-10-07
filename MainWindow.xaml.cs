using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
namespace Papergraph;

public partial class MainWindow : Window
{
    GraphDocument doc=new();readonly string dataDir;
    string file="",editKey="",editorKind="";string? editorId;DateTime editAt;
    bool updating,dirty,dark,ensurePending;long revision;
    readonly Stack<(string Json,string? Scope,string? Region)> undo=[],redo=[];
    readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(800)};
    readonly DispatcherTimer toastTimer=new(){Interval=TimeSpan.FromSeconds(4)};
    sealed record BoardState(string? Scope,string? Region,double Zoom,Point Offset,HashSet<string> Selected,string? Edge,HashSet<string> SelectedRegions,Rect? SelectionBox,(double Zoom,Point Offset)? Detail);
    readonly Stack<BoardState> boards=[];
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    public MainWindow(string directory)
    {
        InitializeComponent();dataDir=directory;Directory.CreateDirectory(dataDir);LoadInitial();dirty|=GraphGroups.ExpandLegacy(doc);dirty|=doc.MaterializeRegions();Graph.Document=doc;
        LoadViewSettings();Localization.Apply(Resources,language);
        InitializeLibrary();ApplyTheme();foreach(var editor in new[]{DocumentTitleBox,CaptionBox,PropositionBox,NotesBox})ConfigureTextMenu(editor);SourceInitialized+=(s,e)=>NativeTitleTheme();
        Graph.SelectionChanged+=SelectionChanged;Graph.BeforeChange+=Remember;Graph.Changed+=()=>Changed();Graph.EnterCircle+=Enter;Graph.CreateNode+=AddNode;Graph.CreateRegion+=AddRegion;Graph.Connect+=AddEdge;
        Graph.EnterRegion+=EnterRegion;Graph.LayoutFinished+=()=>Changed(false);Graph.LayoutFailed+=message=>Notify(Localization.Text("Could not arrange the graph: ")+message);Graph.LayoutNotice+=message=>Notify(message,true);
        Graph.BoxSelectionCompleted+=box=>{ensurePending=false;Graph.Focus();};
        PreviewMouseWheel+=(s,e)=>{if(RouteGraphWheel(e.Delta,e.GetPosition(Graph)))e.Handled=true;};
        Graph.EditRequested+=()=>{if(Graph.SelectionCount==1){SelectionChanged();PropositionBox.Focus();}};Graph.ContextRequested+=()=>ShowContext(Graph,PlacementMode.MousePoint);
        Graph.MouseUp+=(s,e)=>Dispatcher.BeginInvoke(EnsureSelectedVisible,DispatcherPriority.Loaded);
        FitButton.Click+=(s,e)=>{Graph.Focus();Graph.Fit();};FitEditButton.Click+=(s,e)=>{Graph.Focus();Graph.Fit(editing:true);};ArrangeButton.Click+=(s,e)=>ArrangeGraph();NewNodeButton.Click+=(s,e)=>AddNode(FreePosition());BackButton.Click+=(s,e)=>Leave();BuildMenus();
        InitializeConnectionControls();InitializeFullText();InitializeLanguage();
        ThemeButton.Click+=(s,e)=>{dark=!dark;ApplyTheme();SaveViewSettings();};
        MoreButton.Click+=(s,e)=>ShowContext(MoreButton,PlacementMode.Bottom);ColorButton.Click+=(s,e)=>{if(editorKind is "node" or "edge"&&editorId!=null)ShowColors(ColorButton,editorId);};
        LinkButton.Click+=(s,e)=>{if(editorKind is "node" or "region"&&editorId!=null){Graph.LinkMode=true;Graph.LinkStart=editorId;Graph.Cursor=Cursors.Cross;Graph.Focus();}};
        RegionButton.Click+=(s,e)=>RegionAroundSelection();CircleButton.Click+=(s,e)=>CreateCircle();DeleteButton.Click+=(s,e)=>DeleteSelection();CustomRelationButton.Click+=(s,e)=>{if(editorKind=="edge"&&editorId!=null)CustomRelation(editorId);};ToastUndo.Click+=(s,e)=>Undo();
        DocumentTitleBox.TextChanged+=(s,e)=>{if(updating)return;RememberEdit("document");doc.Title=DocumentTitleBox.Text;UpdateLibraryTitle();Changed(false);};
        CaptionBox.TextChanged+=(s,e)=>{if(updating||editorId==null)return;if(editorKind=="node"&&doc.Node(editorId) is Proposition n){RememberEdit("caption:"+n.Id);n.Caption=CaptionBox.Text;}else if(editorKind=="edge"&&doc.Edges.FirstOrDefault(r=>r.Id==editorId) is Relation relation){RememberEdit("edge-caption:"+relation.Id);relation.Caption=CaptionBox.Text;}else if(editorKind=="region"&&doc.Frame(editorId) is Region region){RememberEdit("region-caption:"+region.Id);region.Caption=CaptionBox.Text;}else return;Changed(false);};
        PropositionBox.TextChanged+=(s,e)=>{if(updating||editorId==null)return;if(editorKind=="node"&&doc.Node(editorId) is Proposition n){RememberEdit("title:"+n.Id);n.Title=PropositionBox.Text;}else if(editorKind=="region"&&doc.Regions.FirstOrDefault(r=>r.Id==editorId) is Region r){RememberEdit("region:"+r.Id);r.Title=PropositionBox.Text;}else return;Changed(false);};
        InitializeNotes();
        saveTimer.Tick+=(s,e)=>{saveTimer.Stop();Save();};toastTimer.Tick+=(s,e)=>{toastTimer.Stop();Toast.Visibility=Visibility.Collapsed;};PreviewKeyDown+=Keys;Closing+=OnClosing;
        PreviewMouseDown+=(s,e)=>ResetFrameTap();Deactivated+=(s,e)=>ResetFrameTap();
        Loaded+=(s,e)=>{Graph.Fit(false);RefreshAll();if(dirty)Save();Graph.Focus();};RefreshAll();
    }
    Brush Ui(string key)=>(Brush)Resources[key];
    void ConfigureTextMenu(TextBox editor)
    {
        // WPF's built-in editor popup does not reliably inherit the window's menu template.
        var menu=Menu(editor,PlacementMode.MousePoint);
        void Command(string label,RoutedUICommand command,string shortcut)
        {
            var item=new MenuItem{Header=label,Command=command,CommandTarget=editor,InputGestureText=shortcut,Style=(Style)Resources[typeof(MenuItem)]};Localization.Bind(item,HeaderedItemsControl.HeaderProperty,label);menu.Items.Add(item);
        }
        void Divider()=>menu.Items.Add(new Separator{Style=(Style)Resources[typeof(Separator)]});
        Command("Undo",ApplicationCommands.Undo,"Ctrl+Z");Command("Redo",ApplicationCommands.Redo,"Ctrl+Y");Divider();
        Command("Cut",ApplicationCommands.Cut,"Ctrl+X");Command("Copy",ApplicationCommands.Copy,"Ctrl+C");Command("Paste",ApplicationCommands.Paste,"Ctrl+V");Command("Delete",ApplicationCommands.Delete,"Del");Divider();
        Command("Select all",ApplicationCommands.SelectAll,"Ctrl+A");editor.ContextMenu=menu;
    }
    internal bool RouteGraphWheel(int delta,Point position)
    {
        // Canvas overlays are siblings of Graph, so their wheel events never reach Graph.OnMouseWheel.
        if(delta==0||Graph.IsPreview||position.X<0||position.Y<0||position.X>=Graph.ActualWidth||position.Y>=Graph.ActualHeight)return false;
        Graph.ZoomBy(Math.Exp(delta*.00135),position);return true;
    }
    internal bool RouteFit(Key key,ModifierKeys modifiers,IInputElement? focused)
    {
        if(key!=Key.F||modifiers is not (ModifierKeys.None or ModifierKeys.Shift)||focused is TextBoxBase or RadioButton or MenuItem or System.Windows.Controls.ContextMenu||NotePageTabs.IsKeyboardFocusWithin||GraphTree.IsKeyboardFocusWithin||Graph.IsPreview||Graph.IsInteracting)return false;
        Graph.Fit(editing:modifiers==ModifierKeys.Shift);return true;
    }
    internal bool RouteNodeNavigation(Key key,ModifierKeys modifiers,IInputElement? focused)
    {
        if(!CanRouteNodeNavigation(key,modifiers,focused))return false;
        var direction=key switch {Key.Left=>new Vector(-1,0),Key.Right=>new Vector(1,0),Key.Up=>new Vector(0,-1),_=>new Vector(0,1)};
        if(Graph.NavigateNodes(direction)){ensurePending=false;Graph.Focus();}
        // Consume boundary presses too, so WPF does not move focus into sidebar buttons.
        return true;
    }
    internal bool RouteTitleToggle(Key key,ModifierKeys modifiers,IInputElement? focused,bool repeat=false)
    {
        if(key!=Key.Space||modifiers!=ModifierKeys.None||focused is TextBoxBase or RadioButton or MenuItem or MenuBase||NotePageTabs.IsKeyboardFocusWithin||MainMenu.Items.OfType<MenuItem>().Any(m=>m.IsSubmenuOpen)||Graph.IsInteracting)return false;
        if(!repeat){Graph.ShowCaptions=!Graph.ShowCaptions;Graph.Focus();}return true;
    }
    void ApplyTheme()
    {
        var palette=dark?new[]{"#191C22","#232730","#E1E6ED","#9EABBC","#363E4B","#313A49","#9CC2FF"}:new[]{"#F7F8FA","#FFFFFF","#273747","#7C8997","#DFE5EC","#E9EEF5","#356FBD"};
        var keys=new[]{"CanvasBrush","SurfaceBrush","TextBrush","MutedBrush","BorderBrush","HoverBrush","AccentBrush"};for(int i=0;i<keys.Length;i++)Resources[keys[i]]=GraphStyle.Brush(palette[i]);
        Resources[SystemColors.MenuBrushKey]=Ui("SurfaceBrush");Resources[SystemColors.MenuTextBrushKey]=Ui("TextBrush");Resources[SystemColors.ControlBrushKey]=Ui("SurfaceBrush");Resources[SystemColors.ControlTextBrushKey]=Ui("TextBrush");Resources[SystemColors.HighlightBrushKey]=Ui("HoverBrush");Resources[SystemColors.HighlightTextBrushKey]=Ui("TextBrush");
        Graph.ApplyTheme(dark);ThemeButton.Content=new ThemeGlyph { Sun=dark,Ink=Ui("MutedBrush") };ThemeButton.ToolTip=Localization.Text(dark?"Switch to light theme":"Switch to dark theme");System.Windows.Automation.AutomationProperties.SetName(ThemeButton,(string)ThemeButton.ToolTip);NativeTitleTheme();SelectionChanged();
    }
    void NativeTitleTheme(){var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero)return;try{int value=dark?1:0;DwmSetWindowAttribute(hwnd,20,ref value,4);}catch{}}
    void LoadInitial()
    {
        var recent=Storage.CompatiblePath(dataDir,"recent.txt","\u6700\u8fd1\u6253\u5f00.txt");
        try{if(File.Exists(recent)){var path=File.ReadAllText(recent).Trim();if(!Path.IsPathRooted(path))path=Path.GetFullPath(Path.Combine(dataDir,path));if(File.Exists(path)){doc=GraphDocument.Parse(File.ReadAllText(path));file=path;return;}}var demo=Storage.CompatiblePath(dataDir,"Example.papergraph","\u8d77\u6b65\u793a\u4f8b.yujian");if(File.Exists(demo)){doc=GraphDocument.Parse(File.ReadAllText(demo));file=demo;return;}}
        catch(Exception ex){MessageBox.Show(Localization.Text("Could not read the file. The original is intact. You can open its .bak file to recover a copy.\n\n")+ex.Message,"papergraph");}
        doc=GraphDocument.Demo();file=FreshPath("Example");dirty=true;
    }
    string FreshPath(string title)
    {
        var safe=string.Concat(title.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim();if(safe.Length==0)safe="Untitled paper";if(safe.Length>55)safe=safe[..55];var path=Path.Combine(dataDir,safe+".papergraph");if(File.Exists(path))path=Path.Combine(dataDir,safe+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..4]+".papergraph");return path;
    }
    void Remember(){RecordUndo(true);}
    void RecordUndo(bool cancelLayout){if(cancelLayout)Graph.CancelLayout();undo.Push((doc.Serialize(),Graph.Scope,Graph.BoardRegion));redo.Clear();editKey="";}
    void RememberEdit(string key){if(key!=editKey||(DateTime.Now-editAt).TotalSeconds>2){RecordUndo(false);editKey=key;}editAt=DateTime.Now;}
    void Changed(bool structure=true)
    {
        dirty=true;revision++;SaveDot.Fill=Ui("MutedBrush");SaveDot.ToolTip=Localization.Text("Saving");saveTimer.Stop();saveTimer.Start();
        if(structure){Graph.RefreshData();SelectionChanged();}else Graph.ContentChanged();QueueFullText();
    }
    bool Save()
    {
        if(!dirty)return true;
        try{Storage.Save(file,doc);dirty=false;SaveDot.Fill=GraphStyle.Brush(dark?"#A6BCAF":"#789989");SaveDot.ToolTip=Localization.Text("Saved to ")+file;WriteRecent();return true;}
        catch(Exception ex){SaveDot.Fill=GraphStyle.Brush("#D07974");SaveDot.ToolTip=Localization.Text("Not saved · Ctrl+S to retry");Notify(Localization.Text("Save failed. Your content is still in this window: ")+ex.Message);return false;}
    }
    void WriteRecent(){TrackCurrentGraph();try{var full=Path.GetFullPath(file);var root=Path.GetFullPath(dataDir).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;File.WriteAllText(Path.Combine(dataDir,"recent.txt"),full.StartsWith(root,StringComparison.OrdinalIgnoreCase)?Path.GetRelativePath(dataDir,full):full);}catch{}}
    void Notify(string text,bool canUndo=false){ToastText.Text=text;ToastUndo.Visibility=canUndo?Visibility.Visible:Visibility.Collapsed;Toast.Visibility=Visibility.Visible;toastTimer.Stop();toastTimer.Start();}
    void RefreshAll(){UpdateLibraryTitle();updating=true;DocumentTitleBox.Text=doc.Title;updating=false;Graph.Document=doc;BackButton.Visibility=Graph.Scope==null&&Graph.BoardRegion==null?Visibility.Collapsed:Visibility.Visible;SelectionChanged();QueueFullText();}
    void SetText(TextBox box,string text){box.IsUndoEnabled=false;box.Text=text;box.IsUndoEnabled=true;}
    internal void SelectionChanged()
    {
        updating=true;var visible=Graph.VisibleNodes.Select(n=>n.Id).ToHashSet();var visibleRegions=Graph.VisibleRegions.Select(r=>r.Id).ToHashSet();Graph.Selected.RemoveWhere(id=>!visible.Contains(id));Graph.SelectedRegions.RemoveWhere(id=>!visibleRegions.Contains(id));var previousId=editorId;var previousKind=editorKind;editorId=null;editorKind="";
        NodePanel.Visibility=EdgePanel.Visibility=EditorSymbol.Visibility=CaptionPanel.Visibility=Visibility.Collapsed;MultiBar.Visibility=Graph.GroupSelection?Visibility.Visible:Visibility.Collapsed;ColorButton.Visibility=LinkButton.Visibility=Visibility.Collapsed;EditorHeading.Text="";
        RegionButton.IsEnabled=FrameCreationBounds()!=null;CircleButton.IsEnabled=GraphGroups.CanGroup(doc,Graph.Selected);DeleteButton.IsEnabled=Graph.SelectionCount>0;
        if(Graph.SelectionCount==1&&Graph.Selected.Count==1)
        {
            var node=doc.Node(Graph.Selected.First())!;editorId=node.Id;editorKind="node";NodePanel.Visibility=Visibility.Visible;ColorButton.Visibility=LinkButton.Visibility=Visibility.Visible;NodeColorDot.Kind=node.Kind;NodeColorDot.Stroke=GraphStyle.Brush(GraphMarkColors.Node(doc,node,dark));
            CaptionPanel.Visibility=Visibility.Visible;
            if(previousId!=editorId||previousKind!=editorKind){SetText(CaptionBox,node.Caption);SetText(PropositionBox,node.Title);}EditorHeading.Text=node.Kind=="circle"?"":Localization.Text("Proposition");
            System.Windows.Automation.AutomationProperties.SetName(PropositionBox,"Proposition");
        }
        else if(Graph.SelectionCount==0&&Graph.SelectedEdge is string edgeId&&doc.Edges.FirstOrDefault(e=>e.Id==edgeId) is Relation edge)
        {
            editorId=edge.Id;editorKind="edge";EdgePanel.Visibility=CaptionPanel.Visibility=ColorButton.Visibility=Visibility.Visible;NodeColorDot.Kind="point";NodeColorDot.Stroke=GraphStyle.Brush(GraphMarkColors.Edge(edge,dark));EdgeLabel.Text=Localization.Text(GraphStyle.RelationName(edge.Label));EditorHeading.Text=Localization.Text("Relation");if(previousId!=editorId||previousKind!=editorKind){SetText(CaptionBox,edge.Caption);}BuildArrowChoices(edge);
        }
        else if(Graph.SelectionCount==1&&Graph.SelectedRegion is string regionId&&doc.Regions.FirstOrDefault(r=>r.Id==regionId) is Region region)
        {
            editorId=region.Id;editorKind="region";NodePanel.Visibility=EditorSymbol.Visibility=CaptionPanel.Visibility=LinkButton.Visibility=Visibility.Visible;EditorHeading.Text=Localization.Text("Frame");if(previousId!=editorId||previousKind!=editorKind){SetText(CaptionBox,region.Caption);SetText(PropositionBox,region.Title);}System.Windows.Automation.AutomationProperties.SetName(PropositionBox,"Proposition");
        }
        RefreshNotes();bool open=editorId!=null;EmptyEditor.Visibility=open?Visibility.Collapsed:Visibility.Visible;NotesPanel.Visibility=MoreButton.Visibility=open?Visibility.Visible:Visibility.Collapsed;Graph.RightInset=0;updating=false;Graph.RefreshSelection();
        if(editorKind=="node"&&previousId!=editorId){ensurePending=true;Dispatcher.BeginInvoke(EnsureSelectedVisible,DispatcherPriority.Loaded);}HighlightFullText();
    }
    void EnsureSelectedVisible(){if(!ensurePending||Graph.IsInteracting)return;ensurePending=false;if(editorKind=="node"&&editorId!=null)Graph.EnsureVisible(editorId);}
    void ClearSelection(){Graph.ClearAllSelection();CancelLink();SelectionChanged();Graph.Focus();}
    void FocusCard(){if(editorId==null)return;var box=editorKind=="edge"?NotesBox:PropositionBox;box.Focus();box.CaretIndex=box.Text.Length;}
    void BuildArrowChoices(Relation edge)
    {
        BuildTextRoleChoices(edge);
        DirectionChoices.Children.Clear();foreach(var (direction,symbol,label) in new[]{("reverse","←","Reverse arrow"),("forward","→","Forward arrow"),("both","↔","Both directions")}){var button=new Button{Content=symbol,FontSize=24,ToolTip=Localization.Text(label),Height=42,Margin=new Thickness(2),Background=edge.Direction==direction?Ui("HoverBrush"):Brushes.Transparent};System.Windows.Automation.AutomationProperties.SetName(button,Localization.Text(label));button.Click+=(s,e)=>SetDirection(edge.Id,direction);DirectionChoices.Children.Add(button);}
        ArrowChoices.Children.Clear();foreach(var label in GraphStyle.Relations)
        {
            var button=new Button{Content=new EdgeGlyph{Label=label,Stroke=Ui("TextBrush"),Surface=Ui("SurfaceBrush")},ToolTip=Localization.Text(label),Height=55,Margin=new Thickness(2),Background=edge.Label==label?Ui("HoverBrush"):Brushes.Transparent};System.Windows.Automation.AutomationProperties.SetName(button,Localization.Text(label));var chosen=label;button.Click+=(s,e)=>SetRelation(edge.Id,chosen);ArrowChoices.Children.Add(button);
        }
    }
    internal void AddNode(Point center)
    {
        Remember();center=Graph.ClampToBoard(center);var n=new Proposition{Parent=Graph.Scope,X=center.X-125,Y=center.Y-60};doc.Nodes.Add(n);Graph.Selected=[n.Id];Graph.SelectedEdge=Graph.SelectedRegion=null;CancelLink();Changed();
        // Creating a point must not pan the user's view, even near an edge or after board clamping.
        ensurePending=false;Graph.StartRelaxation([n.Id]);FocusCard();
    }
    Point FreePosition()
    {
        var center=Graph.ToWorld(new Point(Graph.ActualWidth/2,Graph.ActualHeight/2));var nodes=Graph.VisibleNodes;
        var selected=nodes.FirstOrDefault(n=>Graph.Selected.Contains(n.Id));if(selected!=null)center=GraphStyle.Center(selected);else if(nodes.Count>0){var nearest=nodes.MinBy(n=>(GraphStyle.Center(n)-center).Length)!;center=GraphStyle.Center(nearest);}
        center=Graph.ClampToBoard(center);Point best=center;double bestGap=double.NegativeInfinity;
        for(int i=0;i<100;i++){var distance=i==0?0:72*Math.Sqrt(i);var candidate=Graph.ClampToBoard(center+new Vector(Math.Cos(i*2.399)*distance,Math.Sin(i*2.399)*distance));var gap=nodes.Count==0?100:nodes.Min(n=>(GraphStyle.Center(n)-candidate).Length-GraphStyle.Radius(n));if(gap>bestGap){best=candidate;bestGap=gap;}if(gap>68)return candidate;}return best;
    }
    void CancelLink(){Graph.LinkMode=false;Graph.LinkStart=null;Graph.DrawingRegion=false;Graph.Cursor=Cursors.Arrow;Graph.InvalidateVisual();}
    void AddEdge(string from,string to)
    {
        CancelLink();var existing=doc.Edges.FirstOrDefault(e=>e.From==from&&e.To==to&&GraphStyle.RelationName(e.Label)=="Support"&&e.Direction=="forward");if(existing!=null){Graph.Selected.Clear();Graph.SelectedRegion=null;Graph.SelectedEdge=existing.Id;SelectionChanged();return;}
        Remember();var edge=new Relation{From=from,To=to};doc.Edges.Add(edge);Graph.Selected.Clear();Graph.SelectedRegion=null;Graph.SelectedEdge=edge.Id;Changed();if(doc.Node(from)?.Kind=="point"&&doc.Node(to)?.Kind=="point")Graph.StartRelaxation([from,to]);
    }
    ContextMenu Menu(FrameworkElement target,PlacementMode placement=PlacementMode.Bottom)=>new(){PlacementTarget=target,Placement=placement,Resources=Resources,Style=(Style)Resources[typeof(ContextMenu)]};
    MenuItem Item(ItemsControl menu,string text,Action action,string? shortcut=null,bool enabled=true){var item=new MenuItem{Header=text,InputGestureText=shortcut??"",IsEnabled=enabled,Style=(Style)Resources[typeof(MenuItem)]};item.Click+=(s,e)=>action();menu.Items.Add(item);return item;}
    MenuItem SymbolItem(ItemsControl menu,string kind,Action action,string shortcut)
    {
        var item=Item(menu,"",action,shortcut);var glyph=new ObjectGlyph{Kind=kind,HorizontalAlignment=HorizontalAlignment.Left};glyph.SetResourceReference(ObjectGlyph.StrokeProperty,"TextBrush");item.Header=glyph;item.ToolTip=kind=="frame"?"Create □":"Create ◎";System.Windows.Automation.AutomationProperties.SetName(item,kind=="frame"?"Create rectangular group":"Group connected points");return item;
    }
    Rect? FrameCreationBounds()
    {
        var bounds=Graph.SelectionBox??Graph.SelectedBounds();if(bounds.IsEmpty)return null;if(Graph.SelectionBox==null)bounds.Inflate(28,28);bounds.Width=Math.Max(60,bounds.Width);bounds.Height=Math.Max(60,bounds.Height);if(Graph.BoardBounds is Rect parent)bounds.Intersect(parent);return !bounds.IsEmpty&&bounds.Width>=40&&bounds.Height>=40?bounds:null;
    }
    void RegionAroundSelection(){if(FrameCreationBounds() is Rect bounds)AddRegion(bounds);}
    void BuildMenus()
    {
        LocalizedItem(FileMenu,"New paper",NewDocument);LocalizedItem(FileMenu,"Open…",OpenDocument,"Ctrl+O");LocalizedItem(FileMenu,"Save",()=>Save(),"Ctrl+S");LocalizedItem(FileMenu,"Save as…",SaveCopy);LocalizedItem(FileMenu,"Show in folder",ShowGraphFolder);LocalizedItem(FileMenu,"Export Markdown…",Export);
        var undoItem=LocalizedItem(EditMenu,"Undo",Undo,"Ctrl+Z");var redoItem=LocalizedItem(EditMenu,"Redo",Redo,"Ctrl+Y");LocalizedItem(EditMenu,"Delete selection",DeleteSelection,"Delete");EditMenu.SubmenuOpened+=(s,e)=>{undoItem.IsEnabled=undo.Count>0;redoItem.IsEnabled=redo.Count>0;};
        LocalizedItem(InsertMenu,"Proposition",()=>AddNode(FreePosition()),"Ctrl+N");SymbolItem(InsertMenu,"frame",BeginRegion,"R");SymbolItem(InsertMenu,"circle",CreateCircle,"Ctrl+G");
        LocalizedItem(ViewMenu,"Fit all",()=>{Graph.Focus();Graph.Fit();},"F");LocalizedItem(ViewMenu,"Fit for editing",()=>{Graph.Focus();Graph.Fit(editing:true);},"Shift+F");LocalizedItem(ViewMenu,"Focus selection / return",()=>{Graph.Focus();Graph.ToggleDetail();},"Z");LocalizedItem(ViewMenu,"Arrange inside",ArrangeGraph);LocalizedItem(ViewMenu,"Switch theme",()=>ThemeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        BuildConnectionMenu();
        LocalizedItem(HelpMenu,"Controls and symbols",Help);
    }
    void ShowContext(FrameworkElement target,PlacementMode placement)
    {
        var position=placement==PlacementMode.MousePoint?Graph.ToWorld(Mouse.GetPosition(Graph)):PastePosition();
        var menu=CreateGraphContext(target,placement,position);menu.IsOpen=true;
    }
    internal ContextMenu CreateGraphContext(FrameworkElement target,PlacementMode placement,Point position)
    {
        var menu=Menu(target,placement);
        AddClipboardItems(menu,position);
        if(Graph.GroupSelection){LocalizedItem(menu,"Delete",DeleteSelection,"Delete");return menu;}
        if(Graph.Selected.Count==1)
        {
            var n=doc.Node(Graph.Selected.First())!;LocalizedItem(menu,"Edit",FocusCard,"Enter");LocalizedItem(menu,"Mark color…",()=>ShowColors(ColorButton,n.Id));if(n.Kind=="circle"){LocalizedItem(menu,"Enter",()=>Enter(n.Id));LocalizedItem(menu,"Ungroup",()=>DissolveCircle(n.Id));}menu.Items.Add(new Separator());LocalizedItem(menu,"Delete",DeleteSelection,"Delete");
        }
        
        else if(Graph.SelectedRegion is string regionId)
        {
            LocalizedItem(menu,"Edit",FocusCard,"Enter");LocalizedItem(menu,"Connect to…",()=>{Graph.LinkMode=true;Graph.LinkStart=regionId;Graph.Cursor=Cursors.Cross;Graph.Focus();});LocalizedItem(menu,"Enter board",()=>EnterRegion(regionId),"Double-click");
            var region=doc.Regions.First(r=>r.Id==regionId);var members=GraphBoard.Members(doc,region).Select(n=>n.Id).ToHashSet();LocalizedItem(menu,"Group connected contents",()=>{Graph.Selected=members;Graph.SelectedRegion=null;CreateCircle();},null,members.Count>1&&doc.Connected(members,Graph.Scope));
            menu.Items.Add(new Separator());var frame=LocalizedItem(menu,"Move □ only",()=>Graph.MoveRegionContents=false);frame.IsCheckable=true;frame.IsChecked=!Graph.MoveRegionContents;
            var group=LocalizedItem(menu,"Move □ and contents",()=>Graph.MoveRegionContents=true,"Shift + right-drag");group.IsCheckable=true;group.IsChecked=Graph.MoveRegionContents;
            var overlaps=Graph.VisibleRegions.Where(r=>GraphBoard.Bounds(r).IntersectsWith(GraphBoard.Bounds(region))).ToArray();
            if(overlaps.Length>1){var pick=new MenuItem{Header=Localization.Text("Select overlapping □")};foreach(var r in overlaps){var targetId=r.Id;var title=string.IsNullOrWhiteSpace(r.Title)?"□ "+(doc.Regions.IndexOf(r)+1):r.Title.Split('\n')[0];var option=Item(pick,title.Length>24?title[..24]+"…":title,()=>{Graph.SelectedRegion=targetId;Graph.RefreshSelection();SelectionChanged();Graph.Focus();});option.Icon=new System.Windows.Shapes.Rectangle{Width=12,Height=12,RadiusX=3,RadiusY=3,Fill=GraphStyle.Brush(GraphStyle.NodeColor(GraphRegionColors.Base(r),dark))};option.IsChecked=r.Id==regionId;}menu.Items.Add(pick);}
            var shared=members.Where(id=>overlaps.Any(r=>r.Id!=regionId&&GraphBoard.Bounds(r).Contains(GraphStyle.Center(doc.Node(id)!)))).ToHashSet();if(shared.Count>0)LocalizedItem(menu,"Select points in intersection",()=>{Graph.Selected=shared;Graph.SelectedRegion=Graph.SelectedEdge=null;SelectionChanged();Graph.Focus();});
            menu.Items.Add(new Separator());LocalizedItem(menu,"Remove □",DissolveRegion);
        }
        else if(Graph.SelectedEdge is string edge)
        {
            LocalizedItem(menu,"Mark color…",()=>ShowColors(ColorButton,edge));menu.Items.Add(new Separator());
            LocalizedItem(menu,"→ Forward",()=>SetDirection(edge,"forward"));LocalizedItem(menu,"← Reverse",()=>SetDirection(edge,"reverse"));LocalizedItem(menu,"↔ Both directions",()=>SetDirection(edge,"both"));menu.Items.Add(new Separator());foreach(var label in GraphStyle.Relations){var value=label;var item=LocalizedItem(menu,label,()=>SetRelation(edge,value));item.Icon=new EdgeGlyph{Label=label,Stroke=Ui("TextBrush"),Surface=Ui("SurfaceBrush")};}LocalizedItem(menu,"Custom…",()=>CustomRelation(edge));LocalizedItem(menu,"Delete",DeleteSelection);
        }
        else{var center=Graph.ToWorld(Mouse.GetPosition(Graph));LocalizedItem(menu,"New proposition",()=>AddNode(center));SymbolItem(menu,"frame",BeginRegion,"R");LocalizedItem(menu,"Fit all",()=>Graph.Fit(),"F");LocalizedItem(menu,"Fit for editing",()=>Graph.Fit(editing:true),"Shift+F");LocalizedItem(menu,"Arrange inside",ArrangeGraph);LocalizedItem(menu,"Undo",Undo,"Ctrl+Z",undo.Count>0);}
        return menu;
    }
    void SetRelation(string id,string value){var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);if(edge==null||GraphStyle.RelationName(edge.Label)==value)return;if(doc.Edges.Any(e=>e.Id!=id&&e.From==edge.From&&e.To==edge.To&&GraphStyle.RelationName(e.Label)==value&&e.Direction==edge.Direction)){Notify(Localization.Text("This relation already exists"));return;}Remember();edge.Label=value;Changed();}
    void SetDirection(string id,string value){var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);if(edge==null||edge.Direction==value)return;if(doc.Edges.Any(e=>e.Id!=id&&e.From==edge.From&&e.To==edge.To&&GraphStyle.RelationName(e.Label)==GraphStyle.RelationName(edge.Label)&&e.Direction==value)){Notify(Localization.Text("This relation already exists"));return;}Remember();edge.Direction=value;Changed();}
    void ReverseEdge(string id){var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);if(edge==null)return;if(doc.Edges.Any(e=>e.Id!=id&&e.From==edge.To&&e.To==edge.From&&GraphStyle.RelationName(e.Label)==GraphStyle.RelationName(edge.Label))){Notify(Localization.Text("The reverse relation already exists"));return;}Remember();(edge.From,edge.To)=(edge.To,edge.From);Changed();}
    void CustomRelation(string id){var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);if(edge==null)return;var value=Ask(Localization.Text("Relation"),GraphStyle.RelationName(edge.Label));if(value==null)return;if(value.Length>40){Notify(Localization.Text("Relation names can be up to 40 characters"));return;}SetRelation(id,value);}
    void BeginRegion(){CancelLink();Graph.DrawingRegion=true;Graph.Focus();Graph.Cursor=Cursors.Cross;Notify(Localization.Text("Draw a rectangle · Esc to cancel"));}
    void AddRegion(Rect box){if(Graph.BoardBounds is Rect parent)box.Intersect(parent);if(box.IsEmpty||box.Width<40||box.Height<40)return;Remember();var r=new Region{Title="",Color=GraphRegionColors.NewColor(doc.Regions),IsAbsolute=true,Parent=Graph.Scope,X=box.X,Y=box.Y,Width=box.Width,Height=box.Height};doc.Regions.Add(r);Graph.Selected.Clear();Graph.SelectedEdge=null;Graph.SelectedRegion=r.Id;Changed();Toast.Visibility=Visibility.Collapsed;Graph.Focus();}
    void CreateCircle(){if(!GraphGroups.CanGroup(doc,Graph.Selected)){Notify(Localization.Text("Select connected points from the same group, or create a □"));return;}Remember();var roots=GraphGroups.SelectionRoots(doc,Graph.Selected);var frames=Graph.SelectedRegions.ToHashSet();var circle=doc.Collapse(roots.Select(n=>n.Id),roots[0].Parent);circle.Title="";foreach(var r in doc.Regions.Where(r=>frames.Contains(r.Id)&&r.Parent==circle.Parent))r.Parent=circle.Id;Graph.Selected=[circle.Id];Graph.SelectedEdge=Graph.SelectedRegion=null;Changed();FocusCard();}
    BoardState CaptureBoard()=>new(Graph.Scope,Graph.BoardRegion,Graph.Zoom,Graph.Offset,Graph.Selected.ToHashSet(),Graph.SelectedEdge,Graph.SelectedRegions.ToHashSet(),Graph.SelectionBox,Graph.DetailReturnView);
    internal void Enter(string id){if(doc.Node(id)?.Kind!="circle")return;OpenBoard(id,null);}
    internal void EnterRegion(string id){if(!Graph.VisibleRegions.Any(r=>r.Id==id))return;OpenBoard(Graph.Scope,id);}
    void OpenBoard(string? parent,string? region)
    {
        Graph.CancelLayout();boards.Push(CaptureBoard());Graph.SetBoard(parent,region);Graph.Selected.Clear();Graph.SelectedRegion=Graph.SelectedEdge=null;CancelLink();RefreshAll();ensurePending=false;Graph.Fit(false);Graph.Focus();
    }
    internal void Leave()
    {
        if(Graph.Scope==null&&Graph.BoardRegion==null)return;Graph.CancelLayout();
        if(boards.TryPop(out var prior))
        {
            var parent=prior.Scope!=null&&doc.Node(prior.Scope)?.Kind=="circle"?prior.Scope:null;Graph.SetBoard(parent,prior.Region);Graph.Selected=prior.Selected;Graph.SelectedEdge=prior.Edge;Graph.SelectedRegions=prior.SelectedRegions;Graph.SelectionBox=prior.SelectionBox;CancelLink();RefreshAll();ensurePending=false;Graph.SetView(prior.Zoom,prior.Offset);Graph.DetailReturnView=prior.Detail;
        }
        else{Graph.SetBoard(Graph.BoardRegion!=null?Graph.Scope:doc.Node(Graph.Scope)?.Parent,null);ClearSelection();RefreshAll();Graph.Fit(false);}
        Graph.Focus();
    }
    void DissolveCircle(string id){Remember();var children=doc.Visible(id).Select(n=>n.Id).ToHashSet();doc.Dissolve(id);if(doc.Node(id)!=null)children.Add(id);Graph.Selected=children;Changed();Graph.Fit();Notify(doc.Node(id)!=null?Localization.Text("Ungrouped · proposition and notes kept in a point"):Localization.Text("Ungrouped"),true);}
    void DissolveRegion(){if(Graph.SelectedRegion==null)return;Remember();doc.DeleteRegions([Graph.SelectedRegion]);Graph.SelectedRegion=null;Changed();Notify(Localization.Text("□ removed"),true);}
    void DeleteSelection()
    {
        if(Graph.SelectionCount==0&&Graph.SelectedEdge==null)return;Remember();var frames=Graph.SelectedRegions.ToHashSet();if(Graph.Selected.Count>0)doc.DeleteNodes(Graph.Selected);doc.DeleteRegions(frames);if(Graph.SelectedEdge!=null)doc.Edges.RemoveAll(e=>e.Id==Graph.SelectedEdge);Graph.ClearAllSelection();Changed();Notify(Localization.Text("Deleted"),true);Graph.Focus();
    }
    void ArrangeGraph(){Graph.Focus();Graph.ArrangeNaturally();}
    void Undo(){Graph.CancelLayout();if(undo.Count==0)return;redo.Push((doc.Serialize(),Graph.Scope,Graph.BoardRegion));Restore(undo.Pop());}
    void Redo(){Graph.CancelLayout();if(redo.Count==0)return;undo.Push((doc.Serialize(),Graph.Scope,Graph.BoardRegion));Restore(redo.Pop());}
    void Restore((string Json,string? Scope,string? Region) state){doc=GraphDocument.Parse(state.Json);Graph.Document=doc;Graph.SetBoard(state.Scope!=null&&doc.Node(state.Scope)!=null?state.Scope:null,state.Region);Graph.Selected.Clear();Graph.SelectedEdge=Graph.SelectedRegion=null;CancelLink();editKey="";RefreshAll();Changed();}
    void ResetDocument(GraphDocument document,string path,bool needsSave)
    {
        Graph.CancelLayout();saveTimer.Stop();doc=document;file=path;needsSave|=GraphGroups.ExpandLegacy(doc);needsSave|=doc.MaterializeRegions();Graph.Document=doc;Graph.Scope=null;Graph.Selected.Clear();Graph.SelectedRegion=Graph.SelectedEdge=null;undo.Clear();redo.Clear();boards.Clear();CancelLink();Graph.DrawingRegion=false;dirty=needsSave;revision++;editorId=null;RefreshAll();Graph.Fit();if(dirty)Save();else WriteRecent();Graph.Focus();
    }
    bool SaveBeforeSwitch(){Graph.CancelLayout();if(Save())return true;MessageBox.Show(this,Localization.Text("Your changes have not been saved. Please retry or save a copy first."),"papergraph");return false;}
    internal void NewDocument(){if(!SaveBeforeSwitch())return;ResetDocument(new GraphDocument{Title="Untitled graph"},FreshPath("Untitled graph"),true);DocumentTitleBox.Focus();DocumentTitleBox.SelectAll();}
    void OpenDocument()
    {
        if(!SaveBeforeSwitch())return;var dialog=new OpenFileDialog{Title=Localization.Text("Open paper"),Filter="papergraph files and backups|*.papergraph;*.yujian;*.json;*.bak|All files|*.*",InitialDirectory=dataDir};if(dialog.ShowDialog(this)!=true)return;try{var loaded=GraphDocument.Parse(File.ReadAllText(dialog.FileName));if(dialog.FileName.EndsWith(".bak",StringComparison.OrdinalIgnoreCase)){ResetDocument(loaded,FreshPath(loaded.Title+"-recovered"),true);Notify(Localization.Text("Recovered to a new file"));}else ResetDocument(loaded,dialog.FileName,false);}catch(Exception ex){MessageBox.Show(this,Localization.Text("Could not open the file. Your current paper is unchanged.\n\n")+ex.Message,"papergraph");}
    }
    void SaveCopy()
    {
        var name=string.Concat(doc.Title.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim();if(string.IsNullOrWhiteSpace(name))name="Untitled graph";if(name.Length>55)name=name[..55];
        var dialog=new SaveFileDialog{Title="Save graph as",Filter="papergraph document|*.papergraph",FileName=name+".papergraph",InitialDirectory=Path.GetDirectoryName(Path.GetFullPath(file)),DefaultExt=".papergraph",AddExtension=true};
        if(dialog.ShowDialog(this)==true)SaveGraphAs(dialog.FileName);
    }
    void Export(){var dialog=new SaveFileDialog{Title=Localization.Text("Export"),Filter="Markdown|*.md",FileName=Path.GetFileNameWithoutExtension(file)+".md",InitialDirectory=dataDir};if(dialog.ShowDialog(this)!=true)return;try{File.WriteAllText(dialog.FileName,doc.Markdown(),new UTF8Encoding(false));Notify(Localization.Text("Exported"));}catch(Exception ex){MessageBox.Show(this,ex.Message,"Export failed");}}
    string? Ask(string title,string initial)
    {
        var dialog=new Window{Title=title,Width=360,Height=175,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,FontFamily=FontFamily,FontSize=14,Background=Ui("SurfaceBrush"),Foreground=Ui("TextBrush"),ShowInTaskbar=false,Resources=Resources};var panel=new StackPanel{Margin=new Thickness(22)};var input=new TextBox{Text=initial,MinHeight=40,BorderThickness=new Thickness(1)};ConfigureTextMenu(input);panel.Children.Add(input);var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};actions.Children.Add(new Button{Content=Localization.Text("Cancel"),IsCancel=true});var ok=new Button{Content=Localization.Text("OK"),IsDefault=true,Background=Ui("HoverBrush")};ok.Click+=(s,e)=>{if(!string.IsNullOrWhiteSpace(input.Text))dialog.DialogResult=true;};actions.Children.Add(ok);panel.Children.Add(actions);dialog.Content=panel;dialog.Loaded+=(s,e)=>{input.Focus();input.SelectAll();};return dialog.ShowDialog()==true?input.Text.Trim():null;
    }
    void Help(){MessageBox.Show(this,Localization.Text("Click a point, □ or ◎ to edit its proposition and notes.\nThe top two thirds hold the proposition; the bottom third holds notes.\nOnly proposition text changes the point size.\nAdd a short Title to a point or relation to label it on the graph.\nTitles stay readable when zooming out and grow when zooming in.\nPress Space to show or hide all titles.\n\nLeft-drag empty canvas: pan · Wheel: zoom · F: fit all · Shift+F: fit for editing\nArrow keys: select a nearby point · Enter: edit\nDouble-tap an arrow: jump into the nearest frame in that direction\nWhile typing, arrows move the caret · Esc: return to graph\nRight-drag: select · Drag the selection: move\nBottom actions: □ / ◎ / Delete\nCtrl or Shift click: add to selection\nR: draw a □ · Right-drag its border: move\nShift + right-drag: move □ with contents\nRight-drag the lower-right handle: resize □\nDouble-click a □ or ◎: enter its board\nBack / Esc: return · Space: show / hide titles\nZ: focus selection; press again to return\nDrag a small connection handle to create a relation\nClick an edge to change its arrows and write notes below\nCtrl+G: enclose connected points in a ring · Drag its border: move together\nRight-click the ring: ungroup\n\nSolid triangle: Support · Bar: Opposition\nDouble arrow: Inference · Diamond: Qualification\nHollow triangle: Definition · Circle: Association\n\nCtrl+S: save · Ctrl+Z: undo · Ctrl+Y: redo\nUse the sun / moon button to switch themes."),Localization.Text("Controls and symbols"));}
    void Keys(object sender,KeyEventArgs e)
    {
        if(!CanRouteNodeNavigation(e.Key,Keyboard.Modifiers,Keyboard.FocusedElement))ResetFrameTap();
        if(Keyboard.FocusedElement is MenuItem||MainMenu.Items.OfType<MenuItem>().Any(m=>m.IsSubmenuOpen))return;
        bool ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0,shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0,typing=Keyboard.FocusedElement is TextBoxBase;
        if(ctrl&&e.Key==Key.S){Save();e.Handled=true;return;}
        if(NotePageTabs.IsKeyboardFocusWithin)return;
        if(e.Key==Key.Escape){if(Graph.CancelBoxSelection()){}else if(Graph.DrawingRegion){Graph.DrawingRegion=false;Graph.Cursor=Cursors.Arrow;Toast.Visibility=Visibility.Collapsed;}else if(Graph.LinkMode)CancelLink();else if(typing)Graph.Focus();else if(Graph.GroupSelection)ClearSelection();else if(Graph.Scope!=null||Graph.BoardRegion!=null)Leave();else if(editorId!=null||Graph.SelectionCount>0)ClearSelection();e.Handled=true;return;}
        if(typing||GraphTree.IsKeyboardFocusWithin)return;
        if(RouteGraphClipboard(e.Key,Keyboard.Modifiers,Keyboard.FocusedElement)){e.Handled=true;return;}
        if(RouteFit(e.Key,Keyboard.Modifiers,Keyboard.FocusedElement)){e.Handled=true;return;}
        if(RouteArrowKey(e.Key,Keyboard.Modifiers,Keyboard.FocusedElement,e.IsRepeat,e.Timestamp)){e.Handled=true;return;}
        if(RouteTitleToggle(e.Key,Keyboard.Modifiers,Keyboard.FocusedElement,e.IsRepeat)){e.Handled=true;return;}
        if(ctrl&&e.Key==Key.Z)Undo();else if(ctrl&&e.Key==Key.Y)Redo();else if(ctrl&&e.Key==Key.N)AddNode(FreePosition());else if(ctrl&&e.Key==Key.O)OpenDocument();
        else if(ctrl&&e.Key==Key.A){Graph.Selected=Graph.VisibleNodes.Select(n=>n.Id).ToHashSet();Graph.SelectedRegions=Graph.VisibleRegions.Select(r=>r.Id).ToHashSet();Graph.SelectedEdge=null;var bounds=Graph.SelectedBounds();Graph.SelectionBox=bounds.IsEmpty?null:bounds;SelectionChanged();}
        else if(ctrl&&e.Key==Key.G){if(shift)BeginRegion();else CreateCircle();}
        else if(e.Key==Key.R&&!ctrl)BeginRegion();
        else if(e.Key==Key.Enter&&editorId!=null)FocusCard();else if(e.Key==Key.L){Graph.LinkMode=true;Graph.LinkStart=Graph.Selected.Count==1?Graph.Selected.First():Graph.SelectedRegion;Graph.Cursor=Cursors.Cross;}
        else if(e.Key==Key.V)CancelLink();else if(e.Key==Key.Z&&!ctrl)Graph.ToggleDetail(Graph.IsMouseOver?Mouse.GetPosition(Graph):null);else if(e.Key==Key.Delete)DeleteSelection();else return;e.Handled=true;
    }
    void OnClosing(object? sender,CancelEventArgs e){Graph.CancelLayout();saveTimer.Stop();if(!Save())e.Cancel=MessageBox.Show(this,Localization.Text("Your changes have not been saved. Close anyway?"),"papergraph",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes;}
}









