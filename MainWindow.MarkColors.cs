using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace Papergraph;

public partial class MainWindow
{
    string? MarkColor(string id)=>doc.Node(id)?.MarkColor??doc.Edges.FirstOrDefault(e=>e.Id==id)?.MarkColor;
    internal ContextMenu CreateColorMenu(FrameworkElement target,string id)
    {
        var menu=Menu(target);var panel=new StackPanel();var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(4)};
        foreach(var (name,color) in GraphMarkColors.Palette)
        {
            var swatch=new System.Windows.Shapes.Ellipse{Width=23,Height=23,Fill=GraphStyle.Brush(GraphMarkColors.Display(color,dark))};
            var button=new Button{Content=swatch,Padding=new Thickness(6),ToolTip=name,Background=MarkColor(id)==color?Ui("HoverBrush"):Brushes.Transparent};
            AutomationProperties.SetName(button,"Mark "+name);
            button.Click+=(s,e)=>{menu.IsOpen=false;SetMarkColor(id,color);};row.Children.Add(button);
        }
        panel.Children.Add(row);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};
        var automatic=new Button{Content=Localization.Text("Automatic"),FontSize=12,ToolTip=Localization.Text("Remove mark and restore automatic colors")};AutomationProperties.SetName(automatic,"Clear mark color");
        automatic.Click+=(s,e)=>{menu.IsOpen=false;SetMarkColor(id,null);};actions.Children.Add(automatic);
        var custom=new Button{Content=Localization.Text("Custom…"),FontSize=12};custom.Click+=(s,e)=>
        {
            menu.IsOpen=false;var value=Ask(Localization.Text("Mark color"),MarkColor(id)??GraphMarkColors.Palette[0].Color);if(value==null)return;
            if(!GraphMarkColors.Valid(value)){Notify(Localization.Text("Use # followed by six hexadecimal digits"));return;}SetMarkColor(id,value);
        };actions.Children.Add(custom);panel.Children.Add(actions);menu.Items.Add(panel);return menu;
    }
    void ShowColors(FrameworkElement target,string id)
    {
        var menu=CreateColorMenu(target,id);target.ContextMenu=menu;menu.IsOpen=true;
    }
    internal void SetMarkColor(string id,string? color)
    {
        if(!GraphMarkColors.Valid(color))throw new ArgumentException("Invalid mark color.");
        var node=doc.Node(id);var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);
        if(node==null&&edge==null||MarkColor(id)==color)return;
        Remember();if(node!=null)node.MarkColor=color;else edge!.MarkColor=color;Changed();
    }
}
