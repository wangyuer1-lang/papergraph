using System.Windows;
using System.Windows.Input;
namespace Papergraph;

// Mac-only interaction policy; the shared movement, selection, spacing and undo
// machinery remains the same as the Windows canvas.
public sealed partial class GraphSurface
{
    public bool MacPanTool {get;set;}
    public bool MacSpacePan {get;set;}
    bool macPrimaryBox,macPanGesture;

    bool TryBeginMacPan(MouseButtonEventArgs e)
    {
        if(!(MacPanTool||MacSpacePan)||DrawingRegion||LinkMode)return false;
        macPanGesture=panning=true;CaptureMouse();Cursor=Cursors.ScrollAll;e.Handled=true;return true;
    }
    bool TryBeginMacFrameDrag(MouseButtonEventArgs e,Point world,Region? frame,bool multi)
    {
        if(multi||LinkMode)return false;
        var handle=!GroupSelection&&SelectedRegion!=null?regions.FirstOrDefault(r=>r.Id==SelectedRegion&&(world-RegionBounds(r).BottomRight).Length<12/Zoom):null;
        if(handle==null&&frame==null)return false;
        movingRegion=handle??frame!;originalRegion=RegionBounds(movingRegion);
        Selected.Clear();SelectedEdge=null;SelectedRegion=movingRegion.Id;AnnounceSelection();
        resizingRegion=handle!=null;dragging=true;movingContents=MoveRegionContents;
        regionMove=new RegionMove(document,movingRegion,movingContents);
        PrepareDragSpacing(movingContents?GraphBoard.Members(document,movingRegion).Select(n=>n.Id):[]);
        CaptureMouse();Cursor=resizingRegion?Cursors.SizeNWSE:Cursors.SizeAll;e.Handled=true;return true;
    }
    void UpdateMacPointerCursor(Point world)
    {
        if(IsInteracting||DrawingRegion||LinkMode)return;
        if(MacPanTool||MacSpacePan){Cursor=Cursors.ScrollAll;return;}
        if(SelectedRegion!=null&&document.Frame(SelectedRegion) is {} frame&&(world-RegionBounds(frame).BottomRight).Length<12/Zoom)Cursor=Cursors.SizeNWSE;
    }
    internal void MacPanBy(Vector delta)
    {
        if(IsPreview||IsInteracting)return;
        SetView(Zoom,Offset+delta);
    }
    internal void MacZoomAt(double factor,Point anchor)
    {
        if(IsPreview||IsInteracting||!double.IsFinite(factor)||factor<=0)return;
        var world=ToWorld(anchor);var zoom=Math.Clamp(Zoom*factor,MinimumZoom,MaximumZoom);
        SetView(zoom,new Point(anchor.X-world.X*zoom,anchor.Y-world.Y*zoom));
    }
}
