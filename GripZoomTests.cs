using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Papergraph;

public static class GripZoomTests
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static GraphSurface Surface(GraphDocument document)
    {
        var graph=new GraphSurface{Document=document};graph.Measure(new Size(700,240));graph.Arrange(new Rect(0,0,700,240));return graph;
    }
    static RenderTargetBitmap Render(GraphSurface graph)
    {
        graph.UpdateLayout();var bitmap=new RenderTargetBitmap(700,240,96,96,PixelFormats.Pbgra32);bitmap.Render(graph);return bitmap;
    }
    static IEnumerable<Rect> DrawnGrips(Drawing drawing)
    {
        if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var rect in DrawnGrips(child))yield return rect;
        // Only grips have both a filled rounded rectangle and an outline in an unselected graph.
        if(drawing is GeometryDrawing {Geometry:RectangleGeometry rectangle,Brush:not null,Pen:not null})yield return rectangle.Rect;
    }
    public static void Run()
    {
        var crowded=new GraphDocument{Regions=Enumerable.Range(0,24).Select(i=>new Region{Id="frame-"+i,IsAbsolute=true,X=0,Y=i%3*5,Width=120,Height=80}).ToList()};
        crowded.Regions.Add(new Region{Id="narrow",IsAbsolute=true,X=200,Y=0,Width=4,Height=30});
        var graph=Surface(crowded);var original=crowded.Serialize();Dictionary<string,Point>? normal=null;
        foreach(var zoom in new[]{1d,.5,.1,.01,.001,GraphSurface.MinimumZoom,3.2})
        {
            graph.SetView(zoom,new Point(20,70));var grips=graph.RegionGrips();normal??=grips;
            foreach(var frame in crowded.Regions)
            {
                var p=grips[frame.Id];var bounds=graph.RegionBounds(frame);
                Check(double.IsFinite(p.X)&&p.Y==bounds.Top&&p.X>=bounds.Left&&p.X<=bounds.Right,"Crowded grips must stay on their own top border at every zoom");
                Check(p==normal[frame.Id],"Zoom must not push overlapping grips away from their frames");
            }
            if(zoom==.01)foreach(var dark in new[]{false,true})
            {
                graph.ApplyTheme(dark);var bitmap=Render(graph);var pixels=new byte[700*240*4];bitmap.CopyPixels(pixels,700*4,0);
                var background=dark?Color.FromRgb(25,28,34):Color.FromRgb(247,248,250);
                for(int y=45;y<100;y++)for(int x=30;x<700;x++)
                {
                    int pixel=(y*700+x)*4;
                    Check(pixels[pixel]==background.B&&pixels[pixel+1]==background.G&&pixels[pixel+2]==background.R,"Zoomed-out frames must not leave detached grip marks on empty canvas");
                }
            }
        }
        Check(crowded.Serialize()==original,"Grip layout and zoom do not alter graph content");

        var plain=new GraphDocument{Regions=[new(){Id="only",IsAbsolute=true,X=0,Y=0,Width=160,Height=100}]};
        var ring=new GraphDocument{Nodes=[new(){Id="ring",Kind="circle",Expanded=true},new(){Id="a",Parent="ring",X=0,Y=0},new(){Id="b",Parent="ring",X=80,Y=0}],Edges=[new(){From="a",To="b"}]};
        foreach(var sample in new[]{plain,ring})
        {
            graph=Surface(sample);graph.ApplyTheme(true);double? widthAtOne=null,heightAtOne=null;
            foreach(var zoom in new[]{1d,.5,.1,2d})
            {
                graph.SetView(zoom,new Point(30,70));Render(graph);var grip=DrawnGrips(VisualTreeHelper.GetDrawing(graph)).Single();
                widthAtOne??=grip.Width;heightAtOne??=grip.Height;
                Check(Math.Abs(grip.Width*zoom-widthAtOne.Value*zoom)<1e-7&&Math.Abs(grip.Height*zoom-heightAtOne.Value*zoom)<1e-7,"Rendered frame and ring grip dimensions scale with the canvas");
                if(sample==plain)Check(graph.HitRegion(graph.RegionGrips()["only"])?.Id=="only","The displayed grip remains selectable");
            }
        }
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: grip zoom scaling, bounded crowded/narrow frame handles, dark/light raster without stray marks, picking and unchanged graph content.\n");
    }
}
