using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
namespace Papergraph;
public partial class MainWindow
{
    readonly StackPanel textColorToolbar=new(){Orientation=Orientation.Horizontal,Spacing=2,Margin=new(0,5,0,0)};
    readonly List<Button> textColorButtons=[];
    void BuildTextColorToolbar(Grid parent)
    {
        var label=Label("Text color");label.FontSize=12;label.FontWeight=FontWeight.Normal;label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new(4,0,5,0);Paint(()=>label.Foreground=Ui("MutedBrush"));textColorToolbar.Children.Add(label);
        foreach(var (name,color) in GraphTextColors.Palette)
        {
            var b=Button("",()=>ColorBodySelection(color));b.Focusable=false;b.Width=27;b.Height=27;b.Padding=new(6);b.Content=new Border{Width=13,Height=13,CornerRadius=new(7),Background=new SolidColorBrush(Color.Parse(color))};Tip(b,name);textColorButtons.Add(b);textColorToolbar.Children.Add(b);
        }
        var clear=Button("Clear color",()=>ColorBodySelection(null));clear.Focusable=false;clear.FontSize=11;clear.Padding=new(5,3);textColorButtons.Add(clear);textColorToolbar.Children.Add(clear);
        Tip(textColorToolbar,"Select proposition text, then choose a color");Grid.SetRow(textColorToolbar,1);parent.Children.Add(textColorToolbar);
        bodyBox.PropertyChanged+=(s,e)=>{if(e.Property==TextBox.SelectionStartProperty||e.Property==TextBox.SelectionEndProperty)UpdateTextColorToolbar();};
    }
    void UpdateTextColorToolbar()
    {
        textColorToolbar.IsVisible=SelectedObject() is IGraphBody;
        bool enabled=textColorToolbar.IsVisible&&bodyBox.SelectionStart!=bodyBox.SelectionEnd;
        foreach(var button in textColorButtons)button.IsEnabled=enabled;
    }
    void ColorBodySelection(string? color)
    {
        if(SelectedObject() is not IGraphBody item)return;
        int start=Math.Min(bodyBox.SelectionStart,bodyBox.SelectionEnd),length=Math.Abs(bodyBox.SelectionEnd-bodyBox.SelectionStart);
        if(length==0)return;
        var next=GraphTextColors.Apply(item.Title,item.BodyColors,start,length,color);
        if((item.BodyColors??[]).SequenceEqual(next??[]))return;
        Remember();item.BodyColors=next;bodyBox.SetColors(next);Changed(false);RefreshFullText();bodyBox.Focus();
    }
    void UndoBody(bool forward)
    {
        var id=editorId;var kind=editorKind;int start=bodyBox.SelectionStart,end=bodyBox.SelectionEnd;
        if(forward)Redo();else Undo();
        if(id!=null&&doc.HasEndpoint(id)){Graph.ClearAllSelection();if(kind=="node")Graph.Selected=[id];else Graph.SelectedRegion=id;SelectionChanged();bodyBox.Focus();bodyBox.SelectionStart=Math.Min(start,bodyBox.Text?.Length??0);bodyBox.SelectionEnd=Math.Min(end,bodyBox.Text?.Length??0);}
    }
}
