using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace Papergraph;

public partial class MainWindow
{
    internal const uint FrameDoubleTapMilliseconds=350;
    Key frameTapKey=Key.None;
    int frameTapTime;
    string frameTapSelection="";
    GraphDocument? frameTapDocument;
    internal void ResetFrameTap(){frameTapKey=Key.None;frameTapDocument=null;}
    string FrameNavigationState()=>Graph.Scope+"|"+Graph.BoardRegion+"|"+string.Join(",",Graph.Selected.Order())+"|"+string.Join(",",Graph.SelectedRegions.Order())+"|"+Graph.SelectedEdge;
    bool CanRouteNodeNavigation(Key key,ModifierKeys modifiers,IInputElement? focused)=>
        key is Key.Left or Key.Right or Key.Up or Key.Down&&modifiers==ModifierKeys.None&&
        focused is not (TextBoxBase or RadioButton or MenuItem or MenuBase)&&
        !NotePageTabs.IsKeyboardFocusWithin&&!GraphTree.IsKeyboardFocusWithin&&!MainMenu.Items.OfType<MenuItem>().Any(m=>m.IsSubmenuOpen)&&
        !Graph.IsPreview&&!Graph.IsInteracting&&!Graph.DrawingRegion&&!Graph.LinkMode;

    internal bool RouteArrowKey(Key key,ModifierKeys modifiers,IInputElement? focused,bool repeat,int timestamp)
    {
        if(!CanRouteNodeNavigation(key,modifiers,focused)){ResetFrameTap();return false;}
        if(repeat){ResetFrameTap();return RouteNodeNavigation(key,modifiers,focused);}
        var doubleTap=frameTapKey==key&&ReferenceEquals(frameTapDocument,doc)&&
            frameTapSelection==FrameNavigationState()&&unchecked((uint)(timestamp-frameTapTime))<=FrameDoubleTapMilliseconds;
        if(doubleTap)
        {
            ResetFrameTap();
            var direction=key switch{Key.Left=>new Vector(-1,0),Key.Right=>new Vector(1,0),Key.Up=>new Vector(0,-1),_=>new Vector(0,1)};
            if(Graph.FindFrameJump(direction) is { } jump)
            {
                if(Graph.BoardBounds is Rect board&&!board.Contains(GraphBoard.Bounds(jump.Frame)))
                    OpenBoard(jump.Frame.Parent,jump.Frame.Id);
                Graph.SelectFrameJump(jump);ensurePending=false;Graph.Focus();return true;
            }
            return RouteNodeNavigation(key,modifiers,focused);
        }
        RouteNodeNavigation(key,modifiers,focused);
        frameTapKey=key;frameTapTime=timestamp;frameTapSelection=FrameNavigationState();frameTapDocument=doc;
        return true;
    }
}
