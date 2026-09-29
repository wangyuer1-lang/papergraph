using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace Papergraph;

// Text has a readable screen-size floor, shared by drawing and hit testing.
internal static class GraphCaptions
{
    internal const double FontSize=16;
    internal static double TextScale(double zoom)=>Math.Max(1,zoom);
    internal sealed record Placement(string Id,bool IsEdge,Point Anchor,double Angle,Rect TextBounds,double Scale=1)
    {
        public Matrix Transform {get{var m=Matrix.Identity;m.Scale(Scale,Scale);m.Rotate(Angle);m.Translate(Anchor.X,Anchor.Y);return m;}}
        public Rect Bounds=>new MatrixTransform(Transform).TransformBounds(TextBounds);
        public bool Contains(Point screen){var m=Transform;m.Invert();return TextBounds.Contains(m.Transform(screen));}
    }
    internal static FormattedText Measure(string text,Brush brush,double dpi)=>new(
        string.Join(" ",text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)),
        CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),FontSize,brush,dpi);
    internal static Placement Node(string id,Size text,Point center,double radius,double scale=1)
    {
        return new(id,false,center,0,new Rect(new Point(-text.Width/2,radius+8),text),scale);
    }
    internal static Placement Edge(string id,Size text,Point middle,Vector tangent,double scale=1)
    {
        if(tangent.X<0||Math.Abs(tangent.X)<.001&&tangent.Y<0)tangent=-tangent;
        var angle=Math.Atan2(tangent.Y,tangent.X)*180/Math.PI;
        return new(id,true,middle,angle,new Rect(-text.Width/2,-text.Height-7,text.Width,text.Height),scale);
    }
    internal sealed record Candidate(Placement Placement,int Slot,double Preference);
    internal static IEnumerable<Candidate> NodeCandidates(string id,Size text,Point center,double radius,double scale)
    {
        for(int ring=0;ring<3;ring++)for(int side=0;side<8;side++)
        {
            var gap=8+ring*12;var diagonal=radius/Math.Sqrt(2)+gap;
            var offset=side switch {
                0=>new Point(-text.Width/2,radius+gap),1=>new Point(-text.Width/2,-radius-gap-text.Height),
                2=>new Point(radius+gap,-text.Height/2),3=>new Point(-radius-gap-text.Width,-text.Height/2),
                4=>new Point(diagonal,diagonal),5=>new Point(-diagonal-text.Width,diagonal),
                6=>new Point(diagonal,-diagonal-text.Height),_=>new Point(-diagonal-text.Width,-diagonal-text.Height)};
            yield return new(new Placement(id,false,center,0,new Rect(offset,text),scale),ring*8+side,ring*8+side*.3);
        }
    }
    internal static IEnumerable<Candidate> EdgeCandidates(string id,Size text,IReadOnlyList<(Point Point,Vector Tangent)> anchors,double scale)
    {
        for(int anchor=0;anchor<anchors.Count;anchor++)for(int ring=0;ring<3;ring++)for(int side=0;side<2;side++)
        {
            var basis=Edge(id,text,anchors[anchor].Point,anchors[anchor].Tangent,scale);var gap=7+ring*10;
            var bounds=new Rect(-text.Width/2,side==0?-text.Height-gap:gap,text.Width,text.Height);
            yield return new(basis with{TextBounds=bounds},anchor*6+ring*2+side,anchor*3+ring*6+side*.3);
        }
    }
}
