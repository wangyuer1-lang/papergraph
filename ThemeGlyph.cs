using System.Windows;
using System.Windows.Media;
namespace Papergraph;

// WPF host for the same artwork and placement used by Mac/ToolbarGlyph.
public sealed class ThemeGlyph : FrameworkElement
{
    public bool Sun {get;init;}
    public Brush Ink {get;init;}=Brushes.Gray;
    public ThemeGlyph(){Width=ThemeSymbols.Size;Height=ThemeSymbols.Size;IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext dc)
    {
        var p=ThemeSymbols.Place(Sun,ActualWidth,ActualHeight);
        dc.PushTransform(new MatrixTransform(p.Scale,0,0,-p.Scale,p.X,p.Y));
        dc.DrawGeometry(Ink,null,p.Shape);dc.Pop();
    }
}
