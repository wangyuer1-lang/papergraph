using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace Papergraph;

public sealed class AgentLockGlyph : Control
{
    public static readonly StyledProperty<bool> IsLockedProperty=AvaloniaProperty.Register<AgentLockGlyph,bool>(nameof(IsLocked));
    public static readonly StyledProperty<IBrush> InkProperty=AvaloniaProperty.Register<AgentLockGlyph,IBrush>(nameof(Ink),Brushes.Gray);
    public bool IsLocked {get=>GetValue(IsLockedProperty);set=>SetValue(IsLockedProperty,value);}
    public IBrush Ink {get=>GetValue(InkProperty);set=>SetValue(InkProperty,value);}
    static readonly Geometry closed=Geometry.Parse(GraphLockSymbols.Closed),open=Geometry.Parse(GraphLockSymbols.Open);
    static AgentLockGlyph(){AffectsRender<AgentLockGlyph>(IsLockedProperty,InkProperty);}
    public AgentLockGlyph(){IsHitTestVisible=false;}
    public override void Render(DrawingContext dc)
    {
        using var scale=dc.PushTransform(Matrix.CreateScale(Bounds.Width/24,Bounds.Height/24));
        dc.DrawGeometry(null,new Pen(Ink,1.8,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round),IsLocked?closed:open);
    }
}
