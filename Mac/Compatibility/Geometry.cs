// Mutable geometry preserves the existing algorithms' WPF value semantics.
// This assembly contains no WPF dependency. Conversion happens only at the drawing boundary.
namespace System.Windows;
public record struct Point(double X,double Y)
{
    public static Point operator +(Point p,Vector v)=>new(p.X+v.X,p.Y+v.Y);
    public static Point operator -(Point p,Vector v)=>new(p.X-v.X,p.Y-v.Y);
    public static Vector operator -(Point a,Point b)=>new(a.X-b.X,a.Y-b.Y);
    public static implicit operator Avalonia.Point(Point p)=>new(p.X,p.Y);
    public static implicit operator Point(Avalonia.Point p)=>new(p.X,p.Y);
}
public record struct Vector(double X,double Y)
{
    public double LengthSquared=>X*X+Y*Y; public double Length=>Math.Sqrt(LengthSquared);
    public void Normalize(){var length=Length;if(length>0){X/=length;Y/=length;}}
    public static double Multiply(Vector a,Vector b)=>a.X*b.X+a.Y*b.Y;
    public static double CrossProduct(Vector a,Vector b)=>a.X*b.Y-a.Y*b.X;
    public static Vector operator +(Vector a,Vector b)=>new(a.X+b.X,a.Y+b.Y);
    public static Vector operator -(Vector a,Vector b)=>new(a.X-b.X,a.Y-b.Y);
    public static Vector operator -(Vector a)=>new(-a.X,-a.Y);
    public static Vector operator *(Vector v,double n)=>new(v.X*n,v.Y*n);
    public static Vector operator *(double n,Vector v)=>v*n;
    public static Vector operator /(Vector v,double n)=>new(v.X/n,v.Y/n);
}
public record struct Size(double Width,double Height){public static implicit operator Avalonia.Size(Size s)=>new(s.Width,s.Height);}
public record struct Rect
{
    public double X{get;set;} public double Y{get;set;} public double Width{get;set;} public double Height{get;set;}
    public Rect(double x,double y,double width,double height){X=x;Y=y;Width=width;Height=height;}
    public Rect(Point a,Point b):this(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Abs(a.X-b.X),Math.Abs(a.Y-b.Y)){}
    public Rect(Point p,Size s):this(p.X,p.Y,s.Width,s.Height){}
    public static Rect Empty=>new(double.PositiveInfinity,double.PositiveInfinity,double.NegativeInfinity,double.NegativeInfinity);
    public bool IsEmpty=>Width<0;public double Left=>X;public double Top=>Y;public double Right=>X+Width;public double Bottom=>Y+Height;
    public Point TopLeft=>new(Left,Top);public Point TopRight=>new(Right,Top);public Point BottomLeft=>new(Left,Bottom);public Point BottomRight=>new(Right,Bottom);public Size Size=>new(Width,Height);
    public bool Contains(Point p)=>!IsEmpty&&p.X>=Left&&p.X<=Right&&p.Y>=Top&&p.Y<=Bottom;
    public bool Contains(Rect r)=>!r.IsEmpty&&Contains(r.TopLeft)&&Contains(r.BottomRight);
    public bool IntersectsWith(Rect r)=>!IsEmpty&&!r.IsEmpty&&r.Left<=Right&&r.Right>=Left&&r.Top<=Bottom&&r.Bottom>=Top;
    public void Union(Rect r){if(r.IsEmpty)return;if(IsEmpty){this=r;return;}var x=Math.Min(X,r.X);var y=Math.Min(Y,r.Y);this=new(x,y,Math.Max(Right,r.Right)-x,Math.Max(Bottom,r.Bottom)-y);}
    public void Union(Point p)=>Union(new Rect(p,p));
    public static Rect Union(Rect a,Rect b){a.Union(b);return a;}
    public void Intersect(Rect r){if(!IntersectsWith(r)){this=Empty;return;}var x=Math.Max(X,r.X);var y=Math.Max(Y,r.Y);this=new(x,y,Math.Min(Right,r.Right)-x,Math.Min(Bottom,r.Bottom)-y);}
    public static Rect Intersect(Rect a,Rect b){a.Intersect(b);return a;}
    public void Inflate(double x,double y){if(IsEmpty)return;X-=x;Y-=y;Width+=2*x;Height+=2*y;if(Width<0||Height<0)this=Empty;}
    public void Offset(Vector v){if(!IsEmpty){X+=v.X;Y+=v.Y;}}
    public void Offset(double x,double y)=>Offset(new Vector(x,y));
    public static implicit operator Avalonia.Rect(Rect r)=>r.IsEmpty?default:new(r.X,r.Y,r.Width,r.Height);
}
public enum FlowDirection{LeftToRight}
