using System.Windows;
using A=Avalonia.Media;
namespace System.Windows.Media;
public record struct Color(byte A,byte R,byte G,byte B)
{
    public static Color FromRgb(byte r,byte g,byte b)=>new(255,r,g,b);
    public static implicit operator A.Color(Color c)=>new(c.A,c.R,c.G,c.B);
    public static implicit operator Color(A.Color c)=>new(c.A,c.R,c.G,c.B);
}
public static class Colors{public static Color White=>A.Colors.White;public static Color Black=>A.Colors.Black;public static Color Transparent=>A.Colors.Transparent;}
public static class ColorConverter{public static object ConvertFromString(string value)=>(Color)A.Color.Parse(value);}
public abstract class Brush{internal abstract A.IBrush Native{get;}}
public class SolidColorBrush(Color color):Brush
{
    public Color Color{get;set;}=color;internal override A.IBrush Native=>new A.SolidColorBrush(Color);
    public void Freeze(){}
}
public static class Brushes{public static Brush White=>new SolidColorBrush(Colors.White);public static Brush Transparent=>new SolidColorBrush(Colors.Transparent);}
public enum PenLineCap{Flat,Round} public enum PenLineJoin{Miter,Round}
public sealed class Pen(Brush brush,double thickness)
{
    public PenLineCap StartLineCap{get;set;} public PenLineCap EndLineCap{get;set;} public PenLineJoin LineJoin{get;set;}
    public A.IDashStyle? DashStyle{get;set;}
    internal A.Pen Native=>new(brush.Native,thickness,DashStyle,StartLineCap==PenLineCap.Round?A.PenLineCap.Round:A.PenLineCap.Flat,LineJoin==PenLineJoin.Round?A.PenLineJoin.Round:A.PenLineJoin.Miter);
    public void Freeze(){} public Pen Clone()=>(Pen)MemberwiseClone();
}
public static class DashStyles{public static A.IDashStyle Dash=>A.DashStyle.Dash;}
public sealed class StreamGeometry
{
    internal readonly A.StreamGeometry Native=new();
    public GeometryContext Open()=>new(Native.Open());public void Freeze(){}
}
public sealed class GeometryContext(A.StreamGeometryContext context):IDisposable
{
    bool closed;
    public void BeginFigure(Point p,bool filled,bool isClosed){context.BeginFigure(p,filled);closed=isClosed;}
    public void PolyLineTo(Point[] points,bool stroked,bool smooth){foreach(var p in points)context.LineTo(p);}
    public void LineTo(Point p,bool stroked,bool smooth)=>context.LineTo(p);
    public void QuadraticBezierTo(Point c,Point p,bool stroked,bool smooth)=>context.QuadraticBezierTo(c,p);
    public void BezierTo(Point a,Point b,Point c,bool stroked,bool smooth)=>context.CubicBezierTo(a,b,c);
    public void Dispose(){context.EndFigure(closed);context.Dispose();}
}
public struct Matrix
{
    internal Avalonia.Matrix Native;
    public static Matrix Identity=>new(){Native=Avalonia.Matrix.Identity};
    public void Scale(double x,double y)=>Native*=Avalonia.Matrix.CreateScale(x,y);
    public void Rotate(double angle)=>Native*=Avalonia.Matrix.CreateRotation(angle*Math.PI/180);
    public void Translate(double x,double y)=>Native*=Avalonia.Matrix.CreateTranslation(x,y);
    public void Invert()=>Native=Native.Invert();
    public Point Transform(Point p)=>Native.Transform(p);
}
public class MatrixTransform(Matrix matrix)
{
    internal Avalonia.Matrix Native=>matrix.Native;
    public Rect TransformBounds(Rect r){var result=Rect.Empty;foreach(var p in new[]{r.TopLeft,r.TopRight,r.BottomLeft,r.BottomRight})result.Union(matrix.Transform(p));return result;}
}
public sealed class TranslateTransform(double x,double y):MatrixTransform(Create(x,y)){static Matrix Create(double x,double y){var m=Matrix.Identity;m.Translate(x,y);return m;}}
public sealed class ScaleTransform(double x,double y):MatrixTransform(Create(x,y)){static Matrix Create(double x,double y){var m=Matrix.Identity;m.Scale(x,y);return m;}}
public sealed class Typeface(string name){internal A.Typeface Native=>name=="Segoe UI"?A.Typeface.Default:new A.Typeface(name);}
public sealed class FormattedText(string text,System.Globalization.CultureInfo culture,FlowDirection flow,Typeface font,double size,Brush brush,double dpi)
{
    internal A.FormattedText Native=new(text,culture,flow==FlowDirection.LeftToRight?A.FlowDirection.LeftToRight:A.FlowDirection.RightToLeft,font.Native,size,brush.Native);
    internal double PixelsPerDip{get;}=dpi;
    public double WidthIncludingTrailingWhitespace=>Native.WidthIncludingTrailingWhitespace;public double Height=>Native.Height;
}
public sealed class DrawingContext(A.DrawingContext context):IDisposable
{
    readonly Stack<A.DrawingContext.PushedState> states=[];
    public void DrawLine(Pen pen,Point a,Point b)=>context.DrawLine(pen.Native,a,b);
    public void DrawEllipse(Brush? brush,Pen? pen,Point center,double rx,double ry)=>context.DrawEllipse(brush?.Native,pen?.Native,center,rx,ry);
    public void DrawRectangle(Brush? brush,Pen? pen,Rect rect)=>context.DrawRectangle(brush?.Native,pen?.Native,rect);
    public void DrawRoundedRectangle(Brush? brush,Pen? pen,Rect rect,double rx,double ry)=>context.DrawRectangle(brush?.Native,pen?.Native,rect,rx,ry);
    public void DrawGeometry(Brush? brush,Pen? pen,StreamGeometry geometry)=>context.DrawGeometry(brush?.Native,pen?.Native,geometry.Native);
    public void DrawText(FormattedText text,Point p)=>context.DrawText(text.Native,p);
    public void PushTransform(MatrixTransform transform)=>states.Push(context.PushTransform(transform.Native));
    public void PushOpacity(double opacity)=>states.Push(context.PushOpacity(opacity));
    public void Pop()=>states.Pop().Dispose();
    public void Dispose(){while(states.Count>0)Pop();}
}
public static class VisualTreeHelper{public sealed record Dpi(double PixelsPerDip);public static Dpi GetDpi(Avalonia.Visual visual)=>new(Avalonia.Controls.TopLevel.GetTopLevel(visual)?.RenderScaling??1);}
public static class CompositionTarget
{
    static readonly Avalonia.Threading.DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    static event EventHandler? handlers;
    static CompositionTarget(){timer.Tick+=(s,e)=>handlers?.Invoke(s,e);}
    public static event EventHandler Rendering{add{handlers+=value;timer.Start();}remove{handlers-=value;if(handlers==null)timer.Stop();}}
}
