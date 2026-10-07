using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace Papergraph;

// Fixed graphics keep toolbar symbols independent of platform font fallback.
// Theme symbols retain their Windows em size and outlined crescent.
public sealed class ToolbarGlyph : Control
{
    public string Symbol { get; set; }="＋";
    public IBrush Ink { get; set; }=Brushes.Gray;
    public ToolbarGlyph(){IsHitTestVisible=false;}
    public override void Render(DrawingContext dc)
    {
        if(Symbol is "☀" or "☾")
        {
            var p=ThemeSymbols.Place(Symbol=="☀",Bounds.Width,Bounds.Height);
            using var placement=dc.PushTransform(new Matrix(p.Scale,0,0,-p.Scale,p.X,p.Y));
            dc.DrawGeometry(Ink,null,p.Shape);return;
        }
        using var transform=dc.PushTransform(Matrix.CreateScale(Bounds.Width/(Symbol=="▣＋"?36:24),Bounds.Height/24));
        var pen=new Pen(Ink,1.5);
        void Line(double x,double y,double a,double b)=>dc.DrawLine(pen,new(x,y),new(a,b));
        void Plus(double x,double y,double r){Line(x-r,y,x+r,y);Line(x,y-r,x,y+r);}
        switch(Symbol)
        {
            case "＋": case "+": Plus(12,12,8); break;
            case "−": Line(4,12,20,12); break;
            case "▣＋":
                dc.DrawRectangle(null,new Pen(Ink,1.1),new Rect(1,6,12,12));
                dc.DrawRectangle(Ink,null,new Rect(3,8,8,8));Plus(27,12,7);break;
            case "↗": Line(5,19,19,5);Line(9,5,19,5);Line(19,5,19,15);break;
            case "···":
                foreach(var x in new[]{5d,12,19})dc.DrawEllipse(Ink,null,new(x,12),1.4,1.4);break;
            case "‹": Line(15,5,8,12);Line(8,12,15,19);break;
        }
    }
}
