using System.IO;
using System.Windows;
namespace Papergraph;

public static class ViewportTests
{
    public static void Run()
    {
        void Check(bool pass,string reason){if(!pass)throw new Exception(reason);}
        var directory=Path.Combine(Path.GetTempPath(),"yujian-viewport-"+Guid.NewGuid().ToString("N"));
        var window=new MainWindow(directory);var graph=window.Graph;
        graph.Measure(new Size(900,650));graph.Arrange(new Rect(0,0,900,650));graph.SetView(1,new Point(60,40));
        void Settle(){for(int i=0;i<90;i++)graph.AdvanceView(1d/60);}
        void Wheel(int delta,Point anchor)
        {
            var world=graph.ToWorld(anchor);var before=graph.Zoom;
            Check(window.RouteGraphWheel(delta,anchor),"Wheel is accepted anywhere over the canvas");Settle();
            Check(delta>0?graph.Zoom>before:graph.Zoom<before,"Wheel changes camera scale in the requested direction");
            Check((graph.ToScreen(world)-anchor).Length<.001,"Wheel keeps the location under the pointer fixed");
        }
        var original=graph.Document.Serialize();
        Wheel(120,new Point(420,300));Wheel(-120,new Point(420,300));
        Check(Math.Abs(graph.Zoom-1)<.00001,"Opposite wheel steps restore the original scale");
        graph.BeginBoxSelection(new Point(-100,-100),false);graph.UpdateBoxSelection(new Point(1200,900));graph.FinishBoxSelection();
        var selected=graph.Selected.ToHashSet();var frames=graph.SelectedRegions.ToHashSet();var box=graph.SelectionBox;
        Wheel(120,new Point(450,615));Wheel(-120,new Point(450,615));
        Check(graph.GroupSelection&&graph.Selected.SetEquals(selected)&&graph.SelectedRegions.SetEquals(frames)&&graph.SelectionBox==box,"Wheel over the footer preserves mixed selection and the world-space rectangle");
        graph.BeginSelectionMove();graph.EndSelectionMove();Wheel(120,new Point(250,230));
        var before=(graph.Zoom,graph.Offset);
        Check(!window.RouteGraphWheel(120,new Point(950,300)),"Card wheel remains available for scrolling text");
        Check(!window.RouteGraphWheel(120,new Point(200,-20)),"Menu wheel does not zoom the canvas");
        Check(!window.RouteGraphWheel(0,new Point(400,300)),"Horizontal-only wheel cannot start a zoom");Settle();
        Check((graph.Zoom,graph.Offset)==before,"Wheel outside the canvas leaves the camera unchanged");
        graph.IsPreview=true;Check(!window.RouteGraphWheel(120,new Point(400,300)),"Temporary space preview remains read-only");graph.IsPreview=false;
        graph.SetView(.10,new Point(40,20));Wheel(-120,new Point(300,200));Check(graph.Zoom<.10,"Wheel can zoom out past the old minimum");Wheel(120,new Point(300,200));
        graph.SetView(3.2,new Point(40,20));Wheel(-120,new Point(300,200));
        Check(graph.Document.Serialize()==original,"Zoom never changes point, edge or frame data");
        window.Close();File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: pointer-anchored wheel zoom, mixed-selection/footer zoom, post-drag zoom, editor/menu isolation, zoom-limit reversal and document preservation.\n");
    }
}
