using System.Windows;
using System.Windows.Media;
namespace Papergraph;

public sealed class ObjectGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(string),typeof(ObjectGlyph),new FrameworkPropertyMetadata("frame",FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty=DependencyProperty.Register(nameof(Stroke),typeof(Brush),typeof(ObjectGlyph),new FrameworkPropertyMetadata(Brushes.SlateGray,FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind {get=>(string)GetValue(KindProperty);set=>SetValue(KindProperty,value);}
    public Brush Stroke {get=>(Brush)GetValue(StrokeProperty);set=>SetValue(StrokeProperty,value);}
    public ObjectGlyph(){Width=22;Height=22;IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext dc)
    {
        var size=Math.Min(ActualWidth,ActualHeight);var center=new Point(ActualWidth/2,ActualHeight/2);var radius=size*.38;var pen=new Pen(Stroke,1.7);
        if(Kind=="circle"){dc.DrawEllipse(null,pen,center,radius,radius);dc.DrawEllipse(null,pen,center,radius*.58,radius*.58);}
        else if(Kind=="point")dc.DrawEllipse(Stroke,null,center,radius,radius);
        else dc.DrawRoundedRectangle(null,pen,new Rect(center.X-radius,center.Y-radius,radius*2,radius*2),2,2);
    }
}
