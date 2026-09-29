using System.Windows;
using System.Windows.Media;
namespace Papergraph;

public static class GraphStyle
{
    public static readonly string[] Relations=["Support","Opposition","Inference","Qualification","Definition","Association"];
    public const double MaxPointRadius=32;
    public const double MinimumEdgeLength=72;
    public static double MinimumNodeDistance(double radiusA,double radiusB)=>radiusA+radiusB+8+MinimumEdgeLength;
    public static double Radius(int count)
    {
        // Most of the visible range belongs to short claims and paragraphs, with a soft cap for long text.
        var weight=Math.Pow(Math.Max(0,count)/120d,1.2);
        return 12+(MaxPointRadius-12)*weight/(1+weight);
    }
    public static double Radius(Proposition node)=>node.Kind=="circle"?34+4*(1-Math.Exp(-node.Title.Length/1200d)):Radius(node.Title.Length);
    // Preserve size ratios in the overview while keeping small points visible and easy to hit.
    public static double DisplayRadius(double radius,double zoom)=>zoom>=1?radius:Math.Max(6,radius*Math.Sqrt(zoom))/zoom;
    public static double ArrowSize(double radius,double zoom)=>Math.Clamp(DisplayRadius(radius,zoom)*zoom*.60,7.5,11)/zoom;
    public static double EdgeWidth(double zoom,bool selected=false)=>(selected?2.8:2.1)/zoom;
    public static Point Center(Proposition n)=>new(n.X+(n.Kind=="circle"?78:125),n.Y+(n.Kind=="circle"?78:60));
    public static Rect Bounds(Proposition n){var p=Center(n);var r=Radius(n)+(n.Kind=="circle"?7:0);return new Rect(p.X-r,p.Y-r,r*2,r*2);}
    public static Color Mix(Color a,Color b,double t)=>Color.FromRgb((byte)Math.Round(a.R*(1-t)+b.R*t),(byte)Math.Round(a.G*(1-t)+b.G*t),(byte)Math.Round(a.B*(1-t)+b.B*t));
    public static Color NodeColor(string value,bool dark)=>Mix((Color)ColorConverter.ConvertFromString(value),dark?Colors.White:Colors.Black,dark?.60:.50);
    public static SolidColorBrush Brush(string value){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));brush.Freeze();return brush;}
    public static SolidColorBrush Brush(Color value){var brush=new SolidColorBrush(value);brush.Freeze();return brush;}
    public static string RelationName(string label)=>label switch{"\u652f\u6301"=>"Support","\u53cd\u9a73"=>"Opposition","\u63a8\u5bfc"=>"Inference","\u9650\u5b9a"=>"Qualification","\u5b9a\u4e49"=>"Definition","\u76f8\u5173"=>"Association",_=>label};
    public static string Marker(string label)=>RelationName(label) switch{"Support"=>"triangle","Opposition"=>"bar","Inference"=>"double","Qualification"=>"diamond","Definition"=>"hollow","Association"=>"circle",_=>"chevron"};
    public static double Distance(Point point,Point a,Point b){var d=b-a;if(d.LengthSquared<.00001)return(point-a).Length;var t=Math.Clamp(Vector.Multiply(point-a,d)/d.LengthSquared,0,1);return(point-(a+d*t)).Length;}
    public static void DrawTip(DrawingContext dc,string label,Point end,Vector direction,Brush brush,double size,double stroke,Brush background)
    {
        if(direction.Length<.001)return;direction.Normalize();var side=new Vector(-direction.Y,direction.X);var pen=new Pen(brush,stroke){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
        void Line(Point a,Point b)=>dc.DrawLine(pen,a,b);
        void Polygon(Point[] points,bool filled){var geometry=new StreamGeometry();using(var c=geometry.Open()){c.BeginFigure(points[0],true,true);c.PolyLineTo(points.Skip(1).ToArray(),true,false);}geometry.Freeze();dc.DrawGeometry(filled?brush:background,pen,geometry);}
        void Chevron(Point tip){Line(tip-direction*size+side*size*.55,tip);Line(tip,tip-direction*size-side*size*.55);}
        switch(Marker(label))
        {
            case "triangle":Polygon([end,end-direction*size+side*size*.52,end-direction*size-side*size*.52],true);break;
            case "bar":Line(end+side*size*.72,end-side*size*.72);break;
            case "double":Chevron(end);Chevron(end-direction*size*.8);break;
            case "diamond":Polygon([end,end-direction*size*.7+side*size*.5,end-direction*size*1.4,end-direction*size*.7-side*size*.5],false);break;
            case "hollow":Polygon([end,end-direction*size+side*size*.6,end-direction*size-side*size*.6],false);break;
            case "circle":dc.DrawEllipse(background,pen,end-direction*size*.5,size*.48,size*.48);break;
            default:Chevron(end);break;
        }
    }
    public static double Contrast(Color a,Color b)
    {
        double L(Color c){double Linear(byte v){var s=v/255d;return s<=.04045?s/12.92:Math.Pow((s+.055)/1.055,2.4);}return .2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B);}
        var x=L(a);var y=L(b);return(Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
}
public sealed class EdgeGlyph : FrameworkElement
{
    public string Label {get;set;}="Support";
    public Brush Stroke {get;set;}=GraphStyle.Brush("#51647E");
    public Brush Surface {get;set;}=Brushes.White;
    public EdgeGlyph(){Width=35;Height=23;IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext dc){dc.DrawLine(new Pen(Stroke,2.1),new Point(3,12),new Point(24,12));GraphStyle.DrawTip(dc,Label,new Point(30,12),new Vector(1,0),Stroke,8,1.9,Surface);}
}
