using System.Windows;
namespace Papergraph;

public sealed partial class GraphSurface
{
    internal sealed record FrameJump(Region Frame,string? NodeId);

    internal FrameJump? FindFrameJump(Vector direction)
    {
        if(IsPreview||IsInteracting||DrawingRegion||LinkMode||direction.LengthSquared<.001)return null;
        direction.Normalize();
        var chosen=visible.Where(n=>Selected.Contains(n.Id)).ToArray();
        var origin=chosen.Length>0
            ?new Point(chosen.Average(n=>ObjectCenter(n).X),chosen.Average(n=>ObjectCenter(n).Y))
            :ToWorld(new Point(UsableWidth/2,ActualHeight/2));
        // Include neighboring frame boards even when one frame is currently open.
        var available=document.Regions.Where(r=>r.Parent==scope||r.Parent!=null&&visible.Any(n=>n.Id==r.Parent)).ToArray();
        Region? current=SelectedRegion!=null?document.Frame(SelectedRegion):null;
        if(current==null&&chosen.Length==1&&chosen[0].Id==navigationLastNode&&navigationFrame!=null)
            current=available.FirstOrDefault(r=>r.Id==navigationFrame&&GraphBoard.Bounds(r).Contains(origin));
        current??=available.Where(r=>chosen.Length>0&&chosen.All(n=>GraphBoard.Bounds(r).Contains(ObjectCenter(n))))
            .OrderBy(r=>r.Width*r.Height).ThenBy(r=>r.Id,StringComparer.Ordinal).FirstOrDefault();
        current??=document.Frame(boardRegion);
        var anchor=current==null?origin:new Point(current.X+current.Width/2,current.Y+current.Height/2);
        var candidates=available.Where(r=>r.Parent==(current?.Parent??scope)&&r.Id!=current?.Id);
        if(current!=null)
        {
            var bounds=GraphBoard.Bounds(current);
            // Jump to a neighboring frame, not into an enclosing section or one's own subframe.
            candidates=candidates.Where(r=>!bounds.Contains(GraphBoard.Bounds(r))&&!GraphBoard.Bounds(r).Contains(bounds));
        }
        var target=candidates.Select(r=>
        {
            var delta=new Point(r.X+r.Width/2,r.Y+r.Height/2)-anchor;
            var forward=Vector.Multiply(delta,direction);var side=Math.Abs(Vector.CrossProduct(delta,direction));
            return(Frame:r,Forward:forward,Score:forward>.001?forward+2*side+side*side/forward:double.PositiveInfinity);
        }).Where(r=>r.Forward>.001).OrderBy(r=>r.Score).ThenBy(r=>r.Frame.Width*r.Frame.Height)
            .ThenBy(r=>r.Frame.Id,StringComparer.Ordinal).Select(r=>r.Frame).FirstOrDefault();
        if(target==null)return null;
        var memberIds=document.Descendants(GraphBoard.Members(document,target).Select(n=>n.Id));
        var entry=document.Nodes.Where(n=>n.Kind!="circle"&&memberIds.Contains(n.Id)&&GraphBoard.Bounds(target).Contains(GraphStyle.Center(n)))
            .OrderBy(n=>Selected.Contains(n.Id)?1:0).ThenBy(n=>(GraphStyle.Center(n)-origin).LengthSquared).ThenBy(n=>n.Id,StringComparer.Ordinal).FirstOrDefault();
        return new(target,entry?.Id);
    }

    internal void SelectFrameJump(FrameJump jump)
    {
        ClearAllSelection();navigationFrame=jump.Frame.Id;navigationLastNode=jump.NodeId;
        if(jump.NodeId!=null&&visible.Any(n=>n.Id==jump.NodeId))
        {
            Selected=[jump.NodeId];AnnounceSelection();EnsureVisible(jump.NodeId);return;
        }
        // Empty frames remain reachable. An open empty board has no selectable outer border.
        if(regions.Any(r=>r.Id==jump.Frame.Id))SelectedRegion=jump.Frame.Id;
        AnnounceSelection();
        var center=ToScreen(new Point(jump.Frame.X+jump.Frame.Width/2,jump.Frame.Y+jump.Frame.Height/2));
        if(center.X<70||center.X>UsableWidth-70||center.Y<70||center.Y>ActualHeight-70)
            SetView(Zoom,Offset+new Vector(UsableWidth/2-center.X,ActualHeight/2-center.Y),true);
    }
}
