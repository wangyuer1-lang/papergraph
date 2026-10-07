using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using P=System.Windows.Point;
namespace Papergraph;
public partial class MainWindow
{
    Button canvasSelectButton=null!,canvasPanButton=null!,canvasActionsButton=null!,libraryActionsButton=null!;
    readonly TextBlock canvasHint=new(){FontSize=11,Margin=new(8,2,8,5),TextTrimming=TextTrimming.CharacterEllipsis};
    void BuildMacControls()
    {
        graphArea.RowDefinitions=new("Auto,*");Grid.SetRow(Graph,1);
        var bar=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new(5,3)};
        canvasSelectButton=Button("Select",()=>SetCanvasTool(false));Tip(canvasSelectButton,"Drag empty canvas to select · V");
        canvasPanButton=Button("Pan",()=>SetCanvasTool(true));Tip(canvasPanButton,"Drag to move the canvas · H · or hold Space");
        bar.Children.Add(canvasSelectButton);bar.Children.Add(canvasPanButton);
        var minus=GlyphButton("−","Zoom out · ⌘−",()=>ZoomCanvas(1/1.2),30,14);bar.Children.Add(minus);
        var reset=Button("",()=>ZoomCanvas(1/Graph.Zoom));zoomLabel.FontSize=11;zoomLabel.Text="100%";reset.Content=zoomLabel;reset.Width=58;Tip(reset,"Reset zoom to 100%");bar.Children.Add(reset);
        var plus=GlyphButton("+","Zoom in · ⌘+",()=>ZoomCanvas(1.2),30,14);bar.Children.Add(plus);
        canvasActionsButton=Button("Actions",()=>ShowGraphContext(canvasActionsButton));Tip(canvasActionsButton,"Actions for the selection or canvas");bar.Children.Add(canvasActionsButton);
        foreach(var button in bar.Children.OfType<Button>()){button.Height=30;button.Padding=new(8,4);button.FontSize=12;}
        var contents=new DockPanel();DockPanel.SetDock(canvasHint,Dock.Bottom);contents.Children.Add(canvasHint);contents.Children.Add(bar);
        graphArea.Children.Add(Surface(contents,new(0,0,0,1)));
        Paint(()=>canvasHint.Foreground=Ui("MutedBrush"));localize.Add(UpdateMacControls);UpdateMacControls();
    }
    void SetCanvasTool(bool pan)
    {
        if(Graph.IsInteracting)return;
        CancelLink();Graph.MacPanTool=pan;Graph.MacSpacePan=false;UpdateMacControls();Graph.Focus();
    }
    void UpdateMacControls()
    {
        if(canvasSelectButton==null)return;
        canvasSelectButton.Classes.Set("selected",!Graph.MacPanTool&&!Graph.MacSpacePan);
        canvasPanButton.Classes.Set("selected",Graph.MacPanTool||Graph.MacSpacePan);
        var hint=Graph.MacPanTool||Graph.MacSpacePan?"Drag to pan · pinch to zoom · V to select":Graph.SelectedRegion!=null?"Drag the frame border to move · drag its lower-right corner to resize":Graph.GroupSelection?"Drag the selection to move · use the buttons below to group or delete":"Drag empty canvas to select · two-finger scroll to pan · pinch to zoom";
        canvasHint.Text=T(hint);ToolTip.SetTip(canvasHint,T(hint));
        if(!Graph.IsInteracting&&!Graph.DrawingRegion&&!Graph.LinkMode)Graph.Cursor=new(Graph.MacPanTool||Graph.MacSpacePan?StandardCursorType.SizeAll:StandardCursorType.Arrow);
    }
    void ReleaseSpacePan(){Graph.MacSpacePan=false;UpdateMacControls();}
    void ZoomCanvas(double factor)=>Graph.MacZoomAt(factor,new P(Graph.ActualWidth/2,Graph.ActualHeight/2));
    bool CanvasContains(PointerEventArgs e,out P position)
    {
        position=e.GetPosition(Graph);return position.X>=0&&position.Y>=0&&position.X<Graph.ActualWidth&&position.Y<Graph.ActualHeight;
    }
    void CanvasScroll(object? sender,PointerWheelEventArgs e)
    {
        if(!CanvasContains(e,out var p))return;
        if((e.KeyModifiers&(KeyModifiers.Meta|KeyModifiers.Control))!=0)Graph.MacZoomAt(Math.Exp(e.Delta.Y*.162),p);
        else Graph.MacPanBy(new System.Windows.Vector(e.Delta.X*50,e.Delta.Y*50));
        e.Handled=true;
    }
    void CanvasMagnify(object? sender,PointerDeltaEventArgs e)
    {
        if(!CanvasContains(e,out var p))return;
        Graph.MacZoomAt(1+e.Delta.Y,p);e.Handled=true;
    }
}
