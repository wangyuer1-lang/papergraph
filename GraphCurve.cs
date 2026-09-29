using System.Windows;
namespace Papergraph;

public readonly record struct GraphCurve(Point Start,Point Control,Point End)
{
    public Point At(double t){var a=1-t;return new Point(a*a*Start.X+2*a*t*Control.X+t*t*End.X,a*a*Start.Y+2*a*t*Control.Y+t*t*End.Y);}
    public double Anchor(bool atEnd,double radius)
    {
        var center=atEnd?End:Start;double low=0,high=1;for(int i=0;i<16;i++){var middle=(low+high)/2;var t=atEnd?1-middle:middle;if((At(t)-center).Length<radius)low=middle;else high=middle;}return atEnd?1-(low+high)/2:(low+high)/2;
    }
    public double VisibleLength(double startRadius,double endRadius)
    {
        var start=Anchor(false,startRadius);var end=Anchor(true,endRadius);if(start>=end)return 0;double length=0;var previous=At(start);for(int i=1;i<=32;i++){var point=At(start+(end-start)*i/32);length+=(point-previous).Length;previous=point;}return length;
    }
    public static GraphCurve Create(Point a,Point b,Vector normal,double bend)
    {
        if((b-a).Length<.001)b=a+new Vector(.001,0);
        // Zoom never changes routing. Parallel edges get a shallow, distance-bounded lane.
        var limit=(b-a).Length*.22;
        return new GraphCurve(a,a+(b-a)*.5+normal*Math.Clamp(bend,-limit,limit),b);
    }
}
