using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using P=System.Windows.Point;
using R=System.Windows.Rect;
using V=System.Windows.Vector;
namespace Papergraph;
public partial class MainWindow
{
    P pointerPosition;bool pointerInGraph;
    ContextMenu? graphContextMenu;
    Key frameTapKey;long frameTapTime;string frameTapSelection="";GraphDocument? frameTapDocument;
    readonly HashSet<Key> heldKeys=[];
    void InitializeInteraction()
    {
        void TrackPointer(object? sender,PointerEventArgs e){pointerPosition=e.GetPosition(Graph);pointerInGraph=true;}
        Graph.AddHandler(PointerPressedEvent,TrackPointer,Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
        Graph.AddHandler(PointerReleasedEvent,TrackPointer,Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
        Graph.AddHandler(PointerMovedEvent,TrackPointer,Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
        Graph.PointerExited+=(s,e)=>pointerInGraph=false;
        Graph.AddHandler(PointerReleasedEvent,(s,e)=>Avalonia.Threading.Dispatcher.UIThread.Post(EnsureSelectedVisible,Avalonia.Threading.DispatcherPriority.Loaded),Avalonia.Interactivity.RoutingStrategies.Bubble,true);
        AddHandler(PointerPressedEvent,(s,e)=>ResetFrameTap(),Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent,(s,e)=>{heldKeys.Remove(e.Key);if(e.Key==Key.Space)ReleaseSpacePan();},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Deactivated+=(s,e)=>{heldKeys.Clear();ResetFrameTap();ReleaseSpacePan();};
        graphArea.AddHandler(PointerWheelChangedEvent,CanvasScroll,Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Graph.AddHandler(Gestures.PointerTouchPadGestureMagnifyEvent,CanvasMagnify);
    }
    P FreePosition()
    {
        var center=Graph.ToWorld(new P(Graph.ActualWidth/2,Graph.ActualHeight/2));var nodes=Graph.VisibleNodes;var selected=nodes.FirstOrDefault(n=>Graph.Selected.Contains(n.Id));if(selected!=null)center=GraphStyle.Center(selected);else if(nodes.Count>0)center=GraphStyle.Center(nodes.MinBy(n=>(GraphStyle.Center(n)-center).Length)!);center=Graph.ClampToBoard(center);P best=center;double bestGap=double.NegativeInfinity;for(int i=0;i<100;i++){var distance=i==0?0:72*Math.Sqrt(i);var candidate=Graph.ClampToBoard(center+new V(Math.Cos(i*2.399)*distance,Math.Sin(i*2.399)*distance));var gap=nodes.Count==0?100:nodes.Min(n=>(GraphStyle.Center(n)-candidate).Length-GraphStyle.Radius(n));if(gap>bestGap){best=candidate;bestGap=gap;}if(gap>68)return candidate;}return best;
    }
    void AddEdge(string from,string to)
    {
        CancelLink();if(from==to)return;var existing=doc.Edges.FirstOrDefault(e=>e.From==from&&e.To==to&&GraphStyle.RelationName(e.Label)=="Support"&&e.Direction=="forward");if(existing!=null){Graph.ClearAllSelection();Graph.SelectedEdge=existing.Id;SelectionChanged();return;}Remember();var edge=new Relation{From=from,To=to};doc.Edges.Add(edge);Graph.ClearAllSelection();Graph.SelectedEdge=edge.Id;Changed();if(doc.Node(from)?.Kind=="point"&&doc.Node(to)?.Kind=="point")Graph.StartRelaxation([from,to]);
    }
    R? FrameCreationBounds(){var bounds=Graph.SelectionBox??Graph.SelectedBounds();if(bounds.IsEmpty)return null;if(Graph.SelectionBox==null)bounds.Inflate(28,28);bounds.Width=Math.Max(60,bounds.Width);bounds.Height=Math.Max(60,bounds.Height);if(Graph.BoardBounds is R parent)bounds.Intersect(parent);return !bounds.IsEmpty&&bounds.Width>=40&&bounds.Height>=40?bounds:null;}
    void FrameAroundSelection(){if(FrameCreationBounds() is R bounds)AddRegion(bounds);}
    void BeginRegion(){CancelLink();Graph.MacPanTool=false;UpdateMacControls();Graph.DrawingRegion=true;Graph.Cursor=new(StandardCursorType.Cross);Graph.Focus();Notify("Draw a rectangle · Esc to cancel");}
    void AddRegion(R box){if(Graph.BoardBounds is R parent)box.Intersect(parent);if(box.IsEmpty||box.Width<40||box.Height<40)return;Remember();var r=new Region{Title="",Color=GraphRegionColors.NewColor(doc.Regions),IsAbsolute=true,Parent=Graph.Scope,X=box.X,Y=box.Y,Width=box.Width,Height=box.Height};doc.Regions.Add(r);Graph.ClearAllSelection();Graph.SelectedRegion=r.Id;Changed();toast.IsVisible=false;Graph.Focus();}
    void CreateCircle(){if(!GraphGroups.CanGroup(doc,Graph.Selected)){Notify("Select connected points from the same group, or create a □");return;}Remember();var roots=GraphGroups.SelectionRoots(doc,Graph.Selected);var frames=Graph.SelectedRegions.ToHashSet();var circle=doc.Collapse(roots.Select(n=>n.Id),roots[0].Parent);circle.Title="";foreach(var r in doc.Regions.Where(r=>frames.Contains(r.Id)&&r.Parent==circle.Parent))r.Parent=circle.Id;Graph.ClearAllSelection();Graph.Selected=[circle.Id];Changed();FocusCard();}
    void DissolveCircle(){if(editorId==null||doc.Node(editorId)?.Kind!="circle")return;var id=editorId;Remember();var children=doc.Visible(id).Select(n=>n.Id).ToHashSet();doc.Dissolve(id);if(doc.Node(id)!=null)children.Add(id);Graph.ClearAllSelection();Graph.Selected=children;Changed();Graph.Fit();Notify(doc.Node(id)!=null?"Ungrouped · proposition and notes kept in a point":"Ungrouped",true);}
    void DissolveRegion(){if(Graph.SelectedRegion==null)return;Remember();doc.DeleteRegions([Graph.SelectedRegion]);Graph.SelectedRegion=null;Changed();Notify("□ removed",true);}
    void DeleteSelection(){if(Graph.SelectionCount==0&&Graph.SelectedEdge==null)return;Remember();doc.DeleteNodes(Graph.Selected);doc.DeleteRegions(Graph.SelectedRegions);if(Graph.SelectedEdge!=null)doc.Edges.RemoveAll(e=>e.Id==Graph.SelectedEdge);Graph.ClearAllSelection();Changed();Notify("Deleted · Ctrl+Z to undo",true);}
    void BeginLink(){Graph.MacPanTool=false;UpdateMacControls();Graph.LinkMode=true;Graph.LinkStart=Graph.Selected.Count==1?Graph.Selected.First():Graph.SelectedRegion;Graph.Cursor=new(StandardCursorType.Cross);Graph.Focus();}
    void CancelLink(){Graph.LinkMode=false;Graph.LinkStart=null;Graph.DrawingRegion=false;Graph.Cursor=new(StandardCursorType.Arrow);Graph.InvalidateVisual();}
    void ClearSelection(){Graph.ClearAllSelection();CancelLink();SelectionChanged();Graph.Focus();}
    void FocusCard(){if(editorId==null)return;var box=editorKind=="edge"?notesBox:bodyBox;box.Focus();box.CaretIndex=box.Text?.Length??0;}
    BoardState CaptureBoard()=>new(Graph.Scope,Graph.BoardRegion,Graph.Zoom,Graph.Offset,Graph.Selected.ToHashSet(),Graph.SelectedEdge,Graph.SelectedRegions.ToHashSet(),Graph.SelectionBox,Graph.DetailReturnView);
    void EnterBoard(string? scope,string? region){Graph.CancelLayout();boards.Push(CaptureBoard());Graph.SetBoard(scope,region);Graph.ClearAllSelection();CancelLink();RefreshAll();Graph.Fit(false);Graph.Focus();}
    void Leave(){if(Graph.Scope==null&&Graph.BoardRegion==null)return;Graph.CancelLayout();if(boards.TryPop(out var b)){Graph.SetBoard(b.Scope!=null&&doc.Node(b.Scope)?.Kind=="circle"?b.Scope:null,b.Region);Graph.Selected=b.Selected;Graph.SelectedEdge=b.Edge;Graph.SelectedRegions=b.Regions;Graph.SelectionBox=b.Box;CancelLink();RefreshAll();Graph.SetView(b.Zoom,b.Offset);Graph.DetailReturnView=b.Detail;}else{Graph.SetBoard(Graph.BoardRegion!=null?Graph.Scope:doc.Node(Graph.Scope)?.Parent,null);ClearSelection();RefreshAll();Graph.Fit(false);}Graph.Focus();}
    void SetRelation(string id,string value){var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);if(edge==null||GraphStyle.RelationName(edge.Label)==value)return;if(doc.Edges.Any(e=>e.Id!=id&&e.From==edge.From&&e.To==edge.To&&GraphStyle.RelationName(e.Label)==value&&e.Direction==edge.Direction)){Notify("This relation already exists");return;}Remember();edge.Label=value;Changed();}
    void SetDirection(string id,string value){var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);if(edge==null||edge.Direction==value)return;if(doc.Edges.Any(e=>e.Id!=id&&e.From==edge.From&&e.To==edge.To&&GraphStyle.RelationName(e.Label)==GraphStyle.RelationName(edge.Label)&&e.Direction==value)){Notify("This relation already exists");return;}Remember();edge.Direction=value;Changed();}
    void SetTextRole(string id,string role){var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);if(edge==null||(edge.TextRole??"flow")==role)return;Remember();edge.TextRole=role=="flow"?null:role;Changed();}
    async Task CustomRelation(){if(SelectedObject() is not Relation edge)return;var id=edge.Id;var value=await Ask("Relation",GraphStyle.RelationName(edge.Label));if(string.IsNullOrWhiteSpace(value))return;if(value.Length>40){Notify("Relation names can be up to 40 characters");return;}SetRelation(id,value);}
    void SetMark(string id,string? color){if(!GraphMarkColors.Valid(color))throw new ArgumentException("Invalid mark color");var node=doc.Node(id);var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);if(node==null&&edge==null)return;if((node?.MarkColor??edge?.MarkColor)==color)return;Remember();if(node!=null)node.MarkColor=color;else edge!.MarkColor=color;Changed();}
    void ShowColors(Control target)
    {
        if(editorId==null)return;var id=editorId;var current=doc.Node(id)?.MarkColor??doc.Edges.FirstOrDefault(e=>e.Id==id)?.MarkColor;var flyout=new Flyout();var panel=new StackPanel();var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(4)};
        foreach(var (name,color) in GraphMarkColors.Palette){var b=Button("",()=>{flyout.Hide();SetMark(id,color);});b.Content=new Avalonia.Controls.Shapes.Ellipse{Width=23,Height=23,Fill=new SolidColorBrush(GraphMarkColors.Display(color,dark))};b.Padding=new(6);b.Classes.Set("selected",current==color);Tip(b,name);row.Children.Add(b);}panel.Children.Add(row);var actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(Button("Automatic",()=>{flyout.Hide();SetMark(id,null);}));actions.Children.Add(Button("Custom…",()=>{flyout.Hide();_=CustomColor(id,current);}));panel.Children.Add(actions);flyout.Content=panel;flyout.ShowAt(target);
    }
    async Task CustomColor(string id,string? current){var color=await Ask("Mark color",current??GraphMarkColors.Palette[0].Color);if(color==null)return;if(!GraphMarkColors.Valid(color)){Notify("Use # followed by six hexadecimal digits");return;}SetMark(id,color);}
    P PastePosition()=>pointerInGraph?Graph.ToWorld(pointerPosition):Graph.ToWorld(new P(Graph.ActualWidth/2,Graph.ActualHeight/2));
    async Task CopyGraph(){if(Graph.IsPreview||Graph.IsInteracting)return;try{var clipboard=GetTopLevel(this)?.Clipboard;if(clipboard==null)return;var fragment=GraphClipboard.Capture(doc,Graph.Selected,Graph.SelectedRegions);await clipboard.SetTextAsync(fragment.Serialize());Graph.Focus();Notify("Copied · use Actions → Paste or ⌘V");}catch(Exception ex){Notify(ex.Message);}}
    async Task PasteGraph(P? at=null){if(Graph.IsPreview||Graph.IsInteracting)return;try{var clipboard=GetTopLevel(this)?.Clipboard;if(clipboard==null)return;var json=await clipboard.TryGetTextAsync();if(string.IsNullOrWhiteSpace(json)){Notify("Copy a papergraph selection first");return;}var result=GraphClipboard.PreparePaste(doc,GraphFragment.Parse(json),at??PastePosition(),Graph.Scope,Graph.BoardBounds);Remember();doc=result.Document;Graph.Document=doc;CancelLink();Graph.ClearAllSelection();Graph.Selected=result.NodeIds;Graph.SelectedRegions=result.RegionIds;Graph.SelectionBox=result.Bounds;Changed();Graph.Focus();Notify("Pasted",true);}catch(Exception ex){Notify(T("Could not paste: ")+ex.Message);}}
    void ShowGraphContext()=>ShowGraphContext(Graph);
    void ShowGraphContext(Control target)
    {
        var position=PastePosition();var items=new List<object>();if(Graph.SelectionCount>0)items.Add(MenuAction("Copy",()=>_=CopyGraph(),"Meta+C"));items.Add(MenuAction("Paste",()=>_=PasteGraph(position),"Meta+V"));items.Add(new Separator());
        if(Graph.GroupSelection){items.Add(MenuAction("Create □",FrameAroundSelection));var ring=MenuAction("Create ◎",CreateCircle);ring.IsEnabled=GraphGroups.CanGroup(doc,Graph.Selected);items.Add(ring);items.Add(MenuAction("Delete",DeleteSelection,"Back"));}
        else if(SelectedObject() is Proposition node){items.Add(MenuAction("Edit",FocusCard,"Enter"));items.Add(MenuAction("Mark color…",()=>ShowColors(colorButton)));if(node.Kind=="circle"){items.Add(MenuAction("Enter",()=>EnterBoard(node.Id,null)));items.Add(MenuAction("Ungroup",DissolveCircle));}items.Add(new Separator());items.Add(MenuAction("Delete",DeleteSelection,"Back"));}
        else if(SelectedObject() is Region region)
        {
            items.Add(MenuAction("Edit",FocusCard,"Enter"));items.Add(MenuAction("Connect to…",BeginLink));items.Add(MenuAction("Enter board",()=>EnterBoard(Graph.Scope,region.Id)));
            var members=GraphBoard.Members(doc,region).Select(n=>n.Id).ToHashSet();var group=MenuAction("Group connected contents",()=>{Graph.Selected=members;Graph.SelectedRegion=null;CreateCircle();});group.IsEnabled=members.Count>1&&doc.Connected(members,Graph.Scope);items.Add(group);items.Add(new Separator());
            foreach(var (move,title) in new[]{(false,"Move □ only"),(true,"Move □ and contents")}){var option=MenuAction(title,()=>Graph.MoveRegionContents=move);option.ToggleType=MenuItemToggleType.CheckBox;option.IsChecked=Graph.MoveRegionContents==move;items.Add(option);}
            var overlaps=Graph.VisibleRegions.Where(r=>GraphBoard.Bounds(r).IntersectsWith(GraphBoard.Bounds(region))).ToArray();if(overlaps.Length>1){var pick=new MenuItem{Header=T("Select overlapping □")};pick.ItemsSource=overlaps.Select(r=>{var title=string.IsNullOrWhiteSpace(r.Title)?"□ "+(doc.Regions.IndexOf(r)+1):r.Title.Split('\n')[0];var item=MenuAction(title.Length>24?title[..24]+"…":title,()=>{Graph.SelectedRegion=r.Id;SelectionChanged();Graph.Focus();});item.ToggleType=MenuItemToggleType.CheckBox;item.IsChecked=r.Id==region.Id;return item;}).ToArray();items.Add(pick);}
            var shared=members.Where(id=>overlaps.Any(r=>r.Id!=region.Id&&GraphBoard.Bounds(r).Contains(GraphStyle.Center(doc.Node(id)!)))).ToHashSet();if(shared.Count>0)items.Add(MenuAction("Select points in intersection",()=>{Graph.Selected=shared;Graph.SelectedRegion=Graph.SelectedEdge=null;SelectionChanged();Graph.Focus();}));items.Add(new Separator());items.Add(MenuAction("Remove □",DissolveRegion));
        }
        else if(SelectedObject() is Relation edge){items.Add(MenuAction("Mark color…",()=>ShowColors(colorButton)));items.Add(new Separator());foreach(var (value,title) in new[]{("forward","→ Forward"),("reverse","← Reverse"),("both","↔ Both directions")})items.Add(MenuAction(title,()=>SetDirection(edge.Id,value)));items.Add(new Separator());foreach(var value in GraphStyle.Relations){var item=MenuAction(value,()=>SetRelation(edge.Id,value));item.Icon=new EdgeGlyph{Label=value};items.Add(item);}items.Add(MenuAction("Custom…",()=>_=CustomRelation()));items.Add(MenuAction("Delete",DeleteSelection));}
        else{items.Add(MenuAction("New proposition",()=>AddNode(position)));items.Add(MenuAction("Create □",BeginRegion,"R"));items.Add(MenuAction("Fit all",()=>Graph.Fit(),"F"));items.Add(MenuAction("Fit for editing",()=>Graph.Fit(editing:true),"Shift+F"));items.Add(MenuAction("Arrange inside",()=>Graph.ArrangeNaturally()));var undoItem=MenuAction("Undo",Undo,"Meta+Z");undoItem.IsEnabled=undo.Count>0;items.Add(undoItem);}
        graphContextMenu?.Close();
        graphContextMenu=new ContextMenu{ItemsSource=items,Placement=ReferenceEquals(target,Graph)?PlacementMode.Pointer:PlacementMode.BottomEdgeAlignedLeft};
        graphContextMenu.Open(target);
    }
    void ResetFrameTap(){frameTapKey=Key.None;frameTapDocument=null;}
    string FrameNavigationState()=>Graph.Scope+"|"+Graph.BoardRegion+"|"+string.Join(",",Graph.Selected.Order())+"|"+string.Join(",",Graph.SelectedRegions.Order())+"|"+Graph.SelectedEdge;
    void ArrowKey(Key key,bool repeat,long timestamp)
    {
        var direction=key switch{Key.Left=>new V(-1,0),Key.Right=>new V(1,0),Key.Up=>new V(0,-1),_=>new V(0,1)};
        if(repeat){ResetFrameTap();Graph.NavigateNodes(direction);return;}
        var doubleTap=frameTapKey==key&&ReferenceEquals(frameTapDocument,doc)&&frameTapSelection==FrameNavigationState()&&timestamp-frameTapTime<=350;
        if(doubleTap){ResetFrameTap();if(Graph.FindFrameJump(direction) is {} jump){if(Graph.BoardBounds is R board&&!board.Contains(GraphBoard.Bounds(jump.Frame)))EnterBoard(jump.Frame.Parent,jump.Frame.Id);Graph.SelectFrameJump(jump);SelectionChanged();Graph.Focus();return;}Graph.NavigateNodes(direction);return;}
        Graph.NavigateNodes(direction);frameTapKey=key;frameTapTime=timestamp;frameTapSelection=FrameNavigationState();frameTapDocument=doc;
    }
    void Escape(bool typing)
    {
        if(Graph.CancelBoxSelection())return;if(Graph.DrawingRegion){CancelLink();toast.IsVisible=false;}else if(Graph.LinkMode)CancelLink();else if(typing)Graph.Focus();else if(Graph.GroupSelection)ClearSelection();else if(Graph.Scope!=null||Graph.BoardRegion!=null)Leave();else if(editorId!=null||Graph.SelectionCount>0)ClearSelection();
    }
    void Keys(object? sender,KeyEventArgs e)
    {
        var focused=FocusManager?.GetFocusedElement();bool command=(e.KeyModifiers&(KeyModifiers.Meta|KeyModifiers.Control))!=0;bool shift=e.KeyModifiers.HasFlag(KeyModifiers.Shift);bool repeat=!heldKeys.Add(e.Key);bool typing=focused is TextBox;bool arrow=e.Key is Key.Left or Key.Right or Key.Up or Key.Down;
        if(!arrow||command||shift||typing)ResetFrameTap();if(mainMenu.Items.OfType<MenuItem>().Any(m=>m.IsSubMenuOpen)||focused is MenuItem or MenuBase)return;
        if(command&&e.Key==Key.S){Save();e.Handled=true;return;}if(notePages.IsKeyboardFocusWithin)return;
        if(e.Key==Key.Escape){Escape(typing);e.Handled=true;return;}if(typing||libraryTree.IsKeyboardFocusWithin||focused is SelectableTextBlock)return;
        if(Graph.IsPreview)return;
        if(e.Key==Key.Space&&!command){if(shift){if(!repeat)Graph.ShowCaptions=!Graph.ShowCaptions;}else if(!Graph.IsInteracting){Graph.MacSpacePan=true;UpdateMacControls();}e.Handled=true;return;}
        if(Graph.IsInteracting)return;
        Action? action=command?e.Key switch{Key.Z=>shift?Redo:Undo,Key.Y=>Redo,Key.C=>()=>_=CopyGraph(),Key.V=>()=>_=PasteGraph(),Key.N=>()=>AddNode(FreePosition()),Key.O=>()=>_=OpenPicker(),Key.OemPlus or Key.Add=>()=>ZoomCanvas(1.2),Key.OemMinus or Key.Subtract=>()=>ZoomCanvas(1/1.2),Key.D0=>()=>Graph.Fit(),Key.G=>shift?BeginRegion:CreateCircle,Key.A=>()=>{Graph.Selected=Graph.VisibleNodes.Select(n=>n.Id).ToHashSet();Graph.SelectedRegions=Graph.VisibleRegions.Select(r=>r.Id).ToHashSet();Graph.SelectedEdge=null;var bounds=Graph.SelectedBounds();Graph.SelectionBox=bounds.IsEmpty?null:bounds;SelectionChanged();},_=>null}:e.Key switch{
            Key.Delete or Key.Back=>DeleteSelection,Key.F=>()=>Graph.Fit(editing:shift),Key.Z=>()=>Graph.ToggleDetail(pointerInGraph?pointerPosition:null),Key.R=>BeginRegion,Key.Enter=>FocusCard,Key.L=>BeginLink,Key.V=>()=>SetCanvasTool(false),Key.H=>()=>SetCanvasTool(true),
            Key.Left or Key.Right or Key.Up or Key.Down when !shift&&!Graph.DrawingRegion&&!Graph.LinkMode=>()=>ArrowKey(e.Key,repeat,Environment.TickCount64),_=>null};if(action!=null){Safe(action);e.Handled=true;}
    }
    async Task ShowHelp()
    {
        var text="Click a point, frame or ring to edit its proposition and notes. Double-click empty canvas to add a point.\n\nSelect (V): drag empty canvas to select several objects. Drag selected objects to move them. Shift-click or Command-click adds to the selection.\nPan (H): drag to move the canvas. Hold Space for temporary pan; release to return to your current tool.\nTwo-finger scroll: pan. Pinch: zoom around the pointer. Command-scroll or the − / + buttons also zoom; click the percentage to return to 100%.\nActions: commands for the current selection or canvas. The ··· button beside Graphs opens graph/category commands. Secondary click and Control-click remain optional shortcuts.\n\nFrames: drag a border to move, or the selected frame’s lower-right corner to resize. Choose “Move □ and contents” in Actions to carry its points with it.\nUse the selection’s bottom buttons to create a frame, create a ring or delete.\nDouble-click a frame or ring to enter. Esc or the back button returns.\nR: draw a frame. L: connect the selected object, then click its destination. Dragging a small connection handle also creates a relation.\n\nDelete (⌫): delete selected objects. Command-Z: undo; Command-Shift-Z: redo.\nCommand-N: add a proposition. Command-S: save.\nF / Command-0: fit all. Shift-F: fit for editing. Z: focus / return.\nShift-Space: show or hide titles. Arrow keys: select nearby points; double-tap to jump between frames. Enter: edit. Esc: return from text editing to the canvas.\nShortcuts for the canvas are inactive while typing. Notes have four pages; page 1 is yours.\n\nSolid triangle: Support · Bar: Opposition · Double arrow: Inference\nDiamond: Qualification · Hollow triangle: Definition · Circle: Association";
        var dialog=new Window{Title=T("Controls and symbols"),Width=600,Height=640,WindowStartupLocation=WindowStartupLocation.CenterOwner};dialog.Content=new ScrollViewer{Content=new TextBlock{Margin=new(28),TextWrapping=TextWrapping.Wrap,FontSize=14,Text=T(text)}};await dialog.ShowDialog(this);
    }
}
