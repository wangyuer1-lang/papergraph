using System.Windows;
using System.Windows.Media;
namespace Papergraph;

public sealed class AgentLockGlyph : FrameworkElement
{
    public static readonly DependencyProperty IsLockedProperty=DependencyProperty.Register(nameof(IsLocked),typeof(bool),typeof(AgentLockGlyph),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty InkProperty=DependencyProperty.Register(nameof(Ink),typeof(Brush),typeof(AgentLockGlyph),new FrameworkPropertyMetadata(Brushes.Gray,FrameworkPropertyMetadataOptions.AffectsRender));
    public bool IsLocked {get=>(bool)GetValue(IsLockedProperty);set=>SetValue(IsLockedProperty,value);}
    public Brush Ink {get=>(Brush)GetValue(InkProperty);set=>SetValue(InkProperty,value);}
    static readonly Geometry closed=Geometry.Parse(GraphLockSymbols.Closed),open=Geometry.Parse(GraphLockSymbols.Open);
    public AgentLockGlyph(){IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth/24,ActualHeight/24));
        dc.DrawGeometry(null,new Pen(Ink,1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},IsLocked?closed:open);dc.Pop();
    }
}
