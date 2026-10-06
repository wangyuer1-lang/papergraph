using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace Papergraph;

public partial class MainWindow
{
    internal IGraphClipboardStore GraphClipboardStore {get;set;}=new SystemGraphClipboardStore();

    internal bool CanPasteGraph()
    {
        try{var text=GraphClipboardStore.Read();if(text==null)return false;GraphFragment.Parse(text);return true;}
        catch{return false;}
    }
    internal bool CopyGraphSelection()
    {
        if(Graph.IsPreview||Graph.IsInteracting)return false;
        try
        {
            var fragment=GraphClipboard.Capture(doc,Graph.Selected,Graph.SelectedRegions);
            GraphClipboardStore.Write(fragment.Serialize());
            Graph.Focus();Notify("Copied · right-click a destination and choose Paste");return true;
        }
        catch(Exception ex){Notify("Could not copy: "+ex.Message);return false;}
    }
    Point PastePosition()=>Graph.IsMouseOver?Graph.ToWorld(Mouse.GetPosition(Graph)):
        Graph.ToWorld(new Point(Graph.ActualWidth/2,Graph.ActualHeight/2));

    internal bool PasteGraphSelection(Point position)
    {
        if(Graph.IsPreview||Graph.IsInteracting)return false;
        try
        {
            var text=GraphClipboardStore.Read();
            if(text==null){Notify("Copy a papergraph selection first");return false;}
            var result=GraphClipboard.PreparePaste(doc,GraphFragment.Parse(text),position,Graph.Scope,Graph.BoardBounds);
            Remember();doc=result.Document;Graph.Document=doc;CancelLink();Graph.ClearAllSelection();
            Graph.Selected=result.NodeIds;Graph.SelectedRegions=result.RegionIds;
            // Select the entire pasted fragment for immediate rigid movement.
            Graph.SelectionBox=result.Bounds;ensurePending=false;Changed();Graph.Focus();
            Notify("Pasted",true);return true;
        }
        catch(Exception ex){Notify("Could not paste: "+ex.Message);return false;}
    }
    internal bool RouteGraphClipboard(Key key,ModifierKeys modifiers,IInputElement? focused,Point? position=null)
    {
        if(key is not (Key.C or Key.V)||modifiers!=ModifierKeys.Control||focused is TextBoxBase or MenuItem or MenuBase||
           GraphTree.IsKeyboardFocusWithin||Graph.IsPreview||Graph.IsInteracting||MainMenu.Items.OfType<MenuItem>().Any(m=>m.IsSubmenuOpen))return false;
        if(key==Key.C)CopyGraphSelection();else PasteGraphSelection(position??PastePosition());
        return true;
    }
    void AddClipboardItems(ItemsControl menu,Point position)
    {
        if(Graph.SelectionCount>0)Item(menu,"Copy",()=>CopyGraphSelection(),"Ctrl+C");
        Item(menu,"Paste",()=>PasteGraphSelection(position),"Ctrl+V",CanPasteGraph());
        menu.Items.Add(new Separator());
    }
}
