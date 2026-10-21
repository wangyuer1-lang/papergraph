#if !CROSS_PLATFORM
using System.Windows;
using System.Windows.Input;
namespace Papergraph;

public sealed partial class GraphSurface
{
    public bool PanTool { get; set; }
    public bool SpacePan { get; set; }
    bool windowsPanGesture;
    internal bool TryBeginWindowsPan(MouseButtonEventArgs e)
    {
        if (!(PanTool || SpacePan) || DrawingRegion || LinkMode) return false;
        windowsPanGesture = panning = true; CaptureMouse(); Cursor = Cursors.ScrollAll; e.Handled = true; return true;
    }
    internal bool TryBeginWindowsFrameDrag(MouseButtonEventArgs e, Point world, Region? frame, bool multi)
    {
        if (multi || LinkMode) return false;
        var handle = !GroupSelection && SelectedRegion != null ? regions.FirstOrDefault(r => r.Id == SelectedRegion && (world - RegionBounds(r).BottomRight).Length < 12 / Zoom) : null;
        if (handle == null && frame == null) return false;
        movingRegion = handle ?? frame!; originalRegion = RegionBounds(movingRegion);
        Selected.Clear(); SelectedEdge = null; SelectedRegion = movingRegion.Id; AnnounceSelection();
        resizingRegion = handle != null; dragging = true; movingContents = MoveRegionContents;
        regionMove = new RegionMove(document, movingRegion, movingContents);
        PrepareDragSpacing(movingContents ? GraphBoard.Members(document, movingRegion).Select(n => n.Id) : []);
        CaptureMouse(); Cursor = resizingRegion ? Cursors.SizeNWSE : Cursors.SizeAll; e.Handled = true; return true;
    }
    void UpdateWindowsPointerCursor(Point world)
    {
        if (IsInteracting || DrawingRegion || LinkMode) return;
        if (PanTool || SpacePan) Cursor = Cursors.ScrollAll;
        else if (SelectedRegion != null && document.Frame(SelectedRegion) is { } frame && (world - RegionBounds(frame).BottomRight).Length < 12 / Zoom) Cursor = Cursors.SizeNWSE;
    }
}
#endif
