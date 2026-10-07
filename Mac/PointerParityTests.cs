using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using P=System.Windows.Point;
namespace Papergraph;
public partial class MainWindow
{
    async Task RunPointerParityTests()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        SetFullTextVisible(false,false);dark=false;ApplyTheme();SetLanguage("zh-CN");
        ResetDocument(new GraphDocument{Title="鼠标交互测试",Nodes=[new(){Id="a",X=30,Y=30},new(){Id="b",X=260,Y=30}],Regions=[new(){Id="box",X=50,Y=250,Width=400,Height=200,IsAbsolute=true}]},FreshPath("Pointer fixture"),true);
        Graph.CancelLayout();Graph.ClearAllSelection();SelectionChanged();UpdateLayout();
        await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);Graph.SetView(1,new P(0,0));
        using var pointer=new Pointer(903,PointerType.Mouse,true);
        ulong timestamp=1;
        void Press(Control c,Point at,bool right=true,KeyModifiers mods=KeyModifiers.None)=>c.RaiseEvent(new PointerPressedEventArgs(c,pointer,c,at,timestamp++,new PointerPointProperties(right?RawInputModifiers.RightMouseButton:RawInputModifiers.LeftMouseButton,right?PointerUpdateKind.RightButtonPressed:PointerUpdateKind.LeftButtonPressed),mods));
        void Move(Control c,Point at,bool right=true,KeyModifiers mods=KeyModifiers.None)=>c.RaiseEvent(new PointerEventArgs(PointerMovedEvent,c,pointer,c,at,timestamp++,new PointerPointProperties(right?RawInputModifiers.RightMouseButton:RawInputModifiers.LeftMouseButton,PointerUpdateKind.Other),mods));
        void Release(Control c,Point at,bool right=true,KeyModifiers mods=KeyModifiers.None)=>c.RaiseEvent(new PointerReleasedEventArgs(c,pointer,c,at,timestamp++,new PointerPointProperties(RawInputModifiers.None,right?PointerUpdateKind.RightButtonReleased:PointerUpdateKind.LeftButtonReleased),mods,right?MouseButton.Right:MouseButton.Left));
        void Drag(Point from,Point to){Press(Graph,from);Move(Graph,to);Release(Graph,to);}
        async Task Flush(){await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);UpdateLayout();}
        void Screenshot(string name){UpdateLayout();using var bitmap=new RenderTargetBitmap(new PixelSize((int)Width,(int)Height),new Vector(96,96));bitmap.Render(this);bitmap.Save(Path.Combine(dataDir,name));}

        Press(Graph,new(155,90));Release(Graph,new(155,90));await Flush();
        Check(Graph.Selected.SetEquals(["a"])&&graphContextMenu?.IsOpen==true,"Native pointer right-click selects the point and opens its menu");
        Check(graphContextMenu!.GetVisualRoot()==this&&graphContextMenu!.Bounds.Height>0,"Context menu renders inside the visible window overlay");
        Screenshot("pointer-context-light.png");
        Check(ReferenceEquals(FocusManager?.GetFocusedElement(),graphContextMenu),"Open context menu owns keyboard focus");
        graphContextMenu!.RaiseEvent(new KeyEventArgs{RoutedEvent=KeyDownEvent,Key=Key.Escape});await Flush();
        Check(!graphContextMenu.IsOpen&&Graph.Selected.SetEquals(["a"]),"Escape closes the menu without clearing the selected point");
        Press(Graph,new(650,170));Release(Graph,new(650,170));var contextPosition=pointerPosition;await Flush();
        // Native hover may arrive after painting; inspect the position at release.
        Check(Graph.SelectionCount==0&&contextPosition==new P(650,170)&&graphContextMenu.IsOpen,"Empty-canvas context actions use the actual click position without a preceding move");graphContextMenu.Close();
        Press(Graph,new(155,90),false,KeyModifiers.Control);Release(Graph,new(155,90),false,KeyModifiers.Control);await Flush();
        Check(Graph.Selected.SetEquals(["a"])&&graphContextMenu.IsOpen,"macOS Control-click opens the same point menu");graphContextMenu.Close();
        Graph.ClearAllSelection();SelectionChanged();Drag(new(100,40),new(440,150));
        Check(Graph.Selected.SetEquals(["a","b"])&&Graph.GroupSelection&&multiBar.IsVisible&&!graphContextMenu.IsOpen,"Right-drag box-selects points and shows group actions without opening a menu");
        var before=doc.Node("a")!.X;Drag(new(155,90),new(175,110));
        Check(doc.Node("a")!.X==before+20&&!Graph.IsInteracting,"Right-drag moves the complete selection and releases capture");Undo();Graph.ClearAllSelection();SelectionChanged();
        Drag(new(50,280),new(80,310));Check(doc.Frame("box")!.X==80&&doc.Frame("box")!.Y==280,"Right-dragging a frame border moves the frame");
        Drag(new(480,480),new(520,510));Check(doc.Frame("box")!.Width==440&&doc.Frame("box")!.Height==230,"Right-dragging the lower-right frame handle resizes it");
        Press(Graph,new(640,190));Move(Graph,new(700,230));pointer.Capture(null);
        Check(!Graph.IsInteracting,"Interrupted capture cancels the active right-drag");
        Graph.Selected=["a"];SelectionChanged();ShowGraphContext(moreButton);await Flush();
        Check(graphContextMenu.IsOpen&&graphContextMenu.Placement==PlacementMode.BottomEdgeAlignedLeft,"More button opens the same actions below the button");graphContextMenu.Close();

        foreach(var isDark in new[]{false,true})
        {
            dark=isDark;ApplyTheme();await Flush();
            var symbol=(ToolbarGlyph)themeButton.Content!;
            Check(symbol.Symbol==(isDark?"☀":"☾"),"Theme button uses the correct deterministic sun/moon silhouette");
            var center=symbol.TranslatePoint(new(symbol.Bounds.Width/2,symbol.Bounds.Height/2),themeButton)!.Value;
            Check(Math.Abs(center.X-themeButton.Bounds.Width/2)<.5&&Math.Abs(center.Y-themeButton.Bounds.Height/2)<.5,"Theme icon is centred inside its button");
            var plus=this.GetVisualDescendants().OfType<ToolbarGlyph>().Where(g=>g.Symbol=="＋").ToArray();
            Check(plus.Length==2&&plus.All(g=>{var b=(Button)g.GetVisualAncestors().First(a=>a is Button);var c=g.TranslatePoint(new(g.Bounds.Width/2,g.Bounds.Height/2),b)!.Value;return Math.Abs(c.Y-b.Bounds.Height/2)<.5;}),"Toolbar and library plus icons keep a centred baseline");
            Press(themeButton,new(8,8),false);await Flush();
            var presenter=themeButton.GetVisualDescendants().OfType<ContentPresenter>().Single();
            Check(themeButton.IsPressed&&Equals(themeButton.Background,Ui("HoverBrush"))&&presenter.Background==null&&presenter.BorderThickness==default&&themeButton.FocusAdorner==null&&Graph.FocusAdorner==null,"Pressed buttons use the original hover fill with no inherited black presenter or focus frame");
            Screenshot(isDark?"pointer-pressed-dark.png":"pointer-pressed-light.png");
            // Cancel the press outside the button without toggling the theme.
            Move(themeButton,new(-20,-20),false);Release(themeButton,new(-20,-20),false);
        }
        dark=false;ApplyTheme();
        var fileMenu=mainMenu.Items.OfType<MenuItem>().First();fileMenu.IsSubMenuOpen=true;await Flush();
        Check(fileMenu.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single().Placement==PlacementMode.BottomEdgeAlignedLeft,"Top menus open below the toolbar");
        Check(fileMenu.Items.OfType<MenuItem>().First().Bounds.Height>0,"Top menu actions are measured and visible");fileMenu.IsSubMenuOpen=false;
        Graph.ClearAllSelection();SelectionChanged();Save();
    }
}
