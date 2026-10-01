using System.Text.RegularExpressions;
using System.Windows.Media;
namespace Papergraph;

// Manual review marks are independent of the graph's automatic frame colors.
public static class GraphMarkColors
{
    public static readonly (string Name,string Color)[] Palette=[("Red","#E5484D"),("Orange","#C86D09"),("Yellow","#9D8000"),("Green","#168A57"),("Blue","#2F7DE1"),("Purple","#A052D8")];
    public static bool Valid(string? color)=>color==null||Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$");
    public static Color Display(string color,bool dark)
    {
        var value=(Color)ColorConverter.ConvertFromString(color);
        if(dark)value=GraphStyle.Mix(value,Colors.White,.28);
        // Custom colors also remain visible against the current canvas.
        var background=(Color)ColorConverter.ConvertFromString(dark?"#232730":"#F7F8FA");
        for(int i=0;i<20&&GraphStyle.Contrast(value,background)<3;i++)value=GraphStyle.Mix(value,dark?Colors.White:Colors.Black,.1);
        return value;
    }
    public static Color Node(GraphDocument doc,Proposition node,bool dark)=>node.MarkColor is string mark?Display(mark,dark):GraphStyle.NodeColor(node.Kind=="circle"?node.Color:GraphRegionColors.ForNode(node,doc.Regions,doc),dark);
    public static Color Edge(Relation edge,bool dark)=>edge.MarkColor is string mark?Display(mark,dark):(Color)ColorConverter.ConvertFromString(dark?"#9AABC1":"#65788F");
}
