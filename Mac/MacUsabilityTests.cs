using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using P=System.Windows.Point;
namespace Papergraph;
public partial class MainWindow
{
    async Task RunMacUsabilityTests()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        SetFullTextVisible(false,false);SetCanvasTool(false);SetLanguage("zh-CN");dark=false;ApplyTheme();
        ResetDocument(new GraphDocument{Title="Mac 触控板操作",Nodes=[new(){Id="a",X=30,Y=30,Caption="论点一"},new(){Id="b",X=260,Y=30,Caption="论点二"},new(){Id="c",X=100,Y=260,Caption="框内论点"}],Edges=[new(){Id="ab",From="a",To="b"}],Regions=[new(){Id="box",Caption="拖动边框",X=50,Y=250,Width=400,Height=200,IsAbsolute=true}]},FreshPath("Mac interaction"),true);
        Graph.CancelLayout();Graph.ClearAllSelection();SelectionChanged();UpdateLayout();
        await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);Graph.SetView(1,new P(0,0));
        using var pointer=new Pointer(904,PointerType.Mouse,true);ulong time=100;
        void Press(Point p,KeyModifiers mods=KeyModifiers.None,int clicks=1)=>Graph.RaiseEvent(new PointerPressedEventArgs(Graph,pointer,Graph,p,time++,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),mods,clicks));
        void Move(Point p,KeyModifiers mods=KeyModifiers.None)=>Graph.RaiseEvent(new PointerEventArgs(PointerMovedEvent,Graph,pointer,Graph,p,time++,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.Other),mods));
        void Release(Point p,KeyModifiers mods=KeyModifiers.None)=>Graph.RaiseEvent(new PointerReleasedEventArgs(Graph,pointer,Graph,p,time++,new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),mods,MouseButton.Left));
        void Click(Point p,KeyModifiers mods=KeyModifiers.None){Press(p,mods);Release(p,mods);}
        void Drag(Point a,Point b){Press(a);Move(b);Release(b);}
        void Key(Control c,Avalonia.Input.Key key,bool up=false,KeyModifiers mods=KeyModifiers.None)=>c.RaiseEvent(new KeyEventArgs{RoutedEvent=up?KeyUpEvent:KeyDownEvent,Key=key,KeyModifiers=mods});
        void Wheel(Vector delta,KeyModifiers mods=KeyModifiers.None)=>Graph.RaiseEvent(new PointerWheelEventArgs(Graph,pointer,Graph,new(400,200),time++,new PointerPointProperties(),mods,delta));
        async Task Flush(){await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);UpdateLayout();}
        void Screenshot(string name){UpdateLayout();using var bmp=new RenderTargetBitmap(new PixelSize((int)Width,(int)Height),new Vector(96,96));bmp.Render(this);bmp.Save(Path.Combine(dataDir,name));}

        Drag(new(100,40),new(440,150));
        Check(Graph.Selected.SetEquals(["a","b"])&&multiBar.IsVisible,"Primary drag on empty canvas selects several points without a secondary button");
        Drag(new(155,90),new(175,110));
        Check(doc.Node("a")!.X==50&&doc.Node("b")!.X==280,"Primary drag moves all selected points as a group");Undo();
        Click(new(155,90));Click(new(385,90),KeyModifiers.Meta);
        Check(Graph.Selected.SetEquals(["a","b"]),"Command-click extends the selection");
        Click(new(650,170));Check(Graph.SelectionCount==0&&Graph.SelectionBox==null,"Primary click on empty canvas clears the selection");
        Drag(new(50,280),new(80,310));
        Check(doc.Frame("box")!.X==80&&doc.Node("c")!.X==100,"A primary drag moves the frame border without moving its contents by default");Undo();
        Graph.MoveRegionContents=true;Drag(new(50,280),new(70,300));
        Check(doc.Frame("box")!.X==70&&doc.Node("c")!.X==120,"The visible frame action can opt into moving its contents together");Undo();Graph.MoveRegionContents=false;
        Click(new(50,280));Drag(new(450,450),new(490,480));
        Check(doc.Frame("box")!.Width==440&&doc.Frame("box")!.Height==230,"Primary drag on the selected lower-right handle resizes a frame");Undo();
        Click(new(155,90));var originalSelection=Graph.Selected.ToHashSet();var position=doc.Node("a")!.X;
        SetCanvasTool(true);var offset=Graph.Offset;Drag(new(155,90),new(200,120));
        Check(Graph.Offset==offset+new System.Windows.Vector(45,30)&&doc.Node("a")!.X==position&&Graph.Selected.SetEquals(originalSelection),"Pan tool drags the canvas even over a point, preserving objects and selection");
        Click(new(155,90));Check(Graph.Selected.SetEquals(originalSelection),"Clicking with Pan does not deselect objects");SetCanvasTool(false);Graph.SetView(1,new P(0,0));
        Graph.Focus();Key(Graph,Avalonia.Input.Key.Space);offset=Graph.Offset;Drag(new(600,180),new(630,210));Key(Graph,Avalonia.Input.Key.Space,true);
        Check(Graph.Offset==offset+new System.Windows.Vector(30,30)&&!Graph.MacSpacePan&&!Graph.MacPanTool,"Hold-Space drag pans temporarily and release restores Select");
        bodyBox.Focus();Key(bodyBox,Avalonia.Input.Key.Space);Check(!Graph.MacSpacePan,"Space while editing text never activates canvas pan");Key(bodyBox,Avalonia.Input.Key.Space,true);Graph.Focus();
        var captions=Graph.ShowCaptions;Key(Graph,Avalonia.Input.Key.Space,mods:KeyModifiers.Shift);Key(Graph,Avalonia.Input.Key.Space,true,KeyModifiers.Shift);Check(Graph.ShowCaptions!=captions,"Shift-Space retains access to title visibility");
        Graph.SetView(1,new P(0,0));Wheel(new(1,-2));
        Check(Graph.Zoom==1&&Graph.Offset==new P(50,-100),"Two-axis scrolling pans the canvas instead of unexpectedly zooming it");
        var anchor=new P(400,200);var before=Graph.ToWorld(anchor);Wheel(new(0,1),KeyModifiers.Meta);
        Check(Graph.Zoom>1&&(Graph.ToWorld(anchor)-before).Length<1e-6,"Command-scroll zooms around the pointer without shifting its world position");
        var oldZoom=Graph.Zoom;Graph.RaiseEvent(new PointerDeltaEventArgs(Gestures.PointerTouchPadGestureMagnifyEvent,Graph,pointer,Graph,new(400,200),time++,new PointerPointProperties(),KeyModifiers.None,new(.25,.25)));
        Check(Math.Abs(Graph.Zoom-oldZoom*1.25)<1e-6&&(Graph.ToWorld(anchor)-before).Length<1e-6,"Native touchpad magnification preserves the pointer anchor");
        Graph.SetView(1,new P(0,0));Graph.Selected=["a"];SelectionChanged();Graph.Focus();Key(Graph,Avalonia.Input.Key.Back);Key(Graph,Avalonia.Input.Key.Back,true);
        Check(doc.Node("a")==null,"Mac Delete key removes the canvas selection");Undo();
        Graph.Selected=["a"];SelectionChanged();bodyBox.Focus();Key(bodyBox,Avalonia.Input.Key.Back);Key(bodyBox,Avalonia.Input.Key.Back,true);Check(doc.Node("a")!=null,"Delete while editing text does not remove the point");
        Graph.Focus();Click(new(650,170));var count=doc.Nodes.Count;Press(new(650,170),clicks:2);Release(new(650,170));Graph.CancelLayout();
        Check(doc.Nodes.Count==count+1,"Double-click still creates a point in Select mode");Undo();
        Graph.ClearAllSelection();SelectionChanged();canvasActionsButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await Flush();
        Check(graphContextMenu?.IsOpen==true&&graphContextMenu.Items.OfType<MenuItem>().Any(m=>Equals(m.Header,T("New proposition"))),"Visible Actions button exposes empty-canvas commands");graphContextMenu!.Close();
        Graph.Selected=["a","b"];SelectionChanged();canvasActionsButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await Flush();
        Check(graphContextMenu!.Items.OfType<MenuItem>().Any(m=>Equals(m.Header,T("Create □")))&&graphContextMenu.Items.OfType<MenuItem>().Any(m=>Equals(m.Header,T("Create ◎"))),"Visible selection actions include both grouping commands");graphContextMenu.Close();
        libraryActionsButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await Flush();
        var libraryMenu=this.GetVisualDescendants().OfType<ContextMenu>().Single(m=>m.IsOpen);
        Check(libraryMenu.Items.OfType<MenuItem>().Any(m=>Equals(m.Header,T("Move to category"))),"Library ellipsis exposes category actions without secondary click");libraryMenu.Close();
        Graph.ShowCaptions=true;Graph.Fit(false);toast.IsVisible=false;Graph.Selected=["a"];SelectionChanged();await Flush();
        Screenshot("mac-interaction-light.png");dark=true;ApplyTheme();Screenshot("mac-interaction-dark.png");dark=false;ApplyTheme();
        var width=Width;Width=800;await Flush();
        Check(new[]{canvasSelectButton,canvasPanButton,canvasActionsButton}.All(b=>{var p=b.TranslatePoint(new(0,0),graphArea)!.Value;return p.X>=0&&p.X+b.Bounds.Width<=graphArea.Bounds.Width+.5&&p.Y+b.Bounds.Height<=((Control)Graph).Bounds.Top+.5;}),"Canvas controls remain inside the available area at the minimum window width");
        Width=width;await Flush();Graph.ClearAllSelection();SelectionChanged();Save();
    }
}
