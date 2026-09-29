using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Papergraph;

public static class NavigationTests
{
    public static void Run()
    {
        void Check(bool pass,string reason){if(!pass)throw new Exception(reason);}
        Proposition Point(string id,double x,double y,string? parent=null)=>new(){Id=id,Title="Proposition "+id,Caption="Title "+id,Note="Notes "+id,X=x-125,Y=y-60,Parent=parent};
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-navigation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var doc=new GraphDocument{Nodes=[Point("center",450,325),Point("left",250,325),Point("right",650,325),Point("up",450,125),Point("down",450,525),Point("sideways",451,390),new(){Id="group",Kind="circle",Expanded=true,X=822,Y=247,Caption="Argument"},Point("inside-a",800,325,"group"),Point("inside-b",1000,325,"group")],Edges=[new(){Id="edge",From="center",To="right"},new(){Id="internal",From="inside-a",To="inside-b"}],Regions=[new(){Id="frame",IsAbsolute=true,X=350,Y=220,Width=390,Height=160}]};
        var file=Path.Combine(directory,"navigation.papergraph");Storage.Save(file,doc);File.WriteAllText(Path.Combine(directory,"recent.txt"),"navigation.papergraph");
        var window=new MainWindow(directory);var graph=window.Graph;graph.Measure(new Size(900,650));graph.Arrange(new Rect(0,0,900,650));graph.SetView(1,new Point());
        void Select(string id){graph.ClearAllSelection();graph.Selected=[id];window.SelectionChanged();}
        void Press(Key key){Check(window.RouteNodeNavigation(key,ModifierKeys.None,graph),"Unmodified graph arrow is handled");}
        void Settle(){for(int i=0;i<90;i++)graph.AdvanceView(1d/60);}
        Press(Key.Right);Check(graph.Selected.SetEquals(["center"]),"First arrow starts at the point nearest the viewport center");
        foreach(var pair in new[]{(Key.Left,"left"),(Key.Right,"right"),(Key.Up,"up"),(Key.Down,"sideways")}){Select("center");Press(pair.Item1);Check(graph.Selected.SetEquals([pair.Item2]),"Arrow selects a nearby point in its geometric direction");}
        Select("center");Press(Key.Right);Check(window.CaptionBox.Text=="Title right"&&window.PropositionBox.Text=="Proposition right"&&window.NotesBox.Text=="Notes right","Keyboard selection refreshes all writing fields");
        Press(Key.Right);Check(graph.Selected.SetEquals(["inside-a"]),"Visible group contents participate in overview navigation");Press(Key.Right);var view=(graph.Zoom,graph.Offset);Press(Key.Right);Check(graph.Selected.SetEquals(["inside-b"])&&graph.Zoom==view.Zoom,"At the edge, navigation stays put without wrapping");
        Settle();Check(graph.ToScreen(GraphStyle.Center(graph.Document.Node("inside-b")!)).X<=830.1,"Off-screen selection pans into view without changing zoom");
        Select("center");foreach(var editor in new[]{window.CaptionBox,window.PropositionBox,window.NotesBox,window.DocumentTitleBox})Check(!window.RouteNodeNavigation(Key.Right,ModifierKeys.None,editor),"Arrows stay with each text editor");
        foreach(var modifier in new[]{ModifierKeys.Control,ModifierKeys.Shift,ModifierKeys.Alt})Check(!window.RouteNodeNavigation(Key.Right,modifier,graph),"Modified arrow chords remain available to their own controls");
        Check(!window.RouteNodeNavigation(Key.Right,ModifierKeys.None,new MenuItem())&&!window.RouteNodeNavigation(Key.Right,ModifierKeys.None,new ContextMenu()),"Menus retain arrow-key navigation");
        Check(graph.Selected.SetEquals(["center"]),"Text and menu navigation never changes the selected point");
        graph.IsPreview=true;Check(!window.RouteNodeNavigation(Key.Right,ModifierKeys.None,graph),"Temporary preview ignores node navigation");graph.IsPreview=false;
        graph.BeginSelectionMove();Check(!window.RouteNodeNavigation(Key.Right,ModifierKeys.None,graph),"Dragging is not interrupted by node navigation");graph.EndSelectionMove();
        graph.LinkMode=true;Check(!window.RouteNodeNavigation(Key.Right,ModifierKeys.None,graph),"Connection gestures retain their endpoints");graph.LinkMode=false;
        graph.ClearAllSelection();graph.SelectedEdge="edge";Press(Key.Left);Check(graph.Selected.SetEquals(["center"])&&graph.SelectedEdge==null,"Relation selection navigates from its midpoint to an endpoint");
        graph.ClearAllSelection();graph.Selected=["center","right"];graph.SelectedRegions=["frame"];graph.SelectionBox=new Rect(200,100,600,500);Press(Key.Left);Check(graph.Selected.SetEquals(["left"])&&graph.SelectedRegions.Count==0&&graph.SelectionBox==null,"An arrow replaces mixed selection with one directional point");
        graph.SetBoard("group",null);graph.ClearAllSelection();graph.SetView(1,new Point());Press(Key.Right);Press(Key.Right);Check(graph.Selected.SetEquals(["inside-b"]),"Navigation inside a double ring stays on its board");Press(Key.Down);Check(graph.Selected.SetEquals(["inside-b"]),"Hidden outer points are never selected");
        graph.SetBoard(null,"frame");graph.ClearAllSelection();graph.SetView(1,new Point());Press(Key.Right);Press(Key.Right);Check(graph.Selected.SetEquals(["right"]),"Frame-board navigation only visits included points");Press(Key.Right);Check(graph.Selected.SetEquals(["right"]),"Frame navigation cannot escape to an outside point");
        Check(graph.Document.Serialize()==doc.Serialize(),"Keyboard selection and camera movement never mutate saved content or point positions");window.Close();Check(GraphDocument.Parse(File.ReadAllText(file)).Serialize()==doc.Serialize(),"Navigation leaves the saved document unchanged");
        graph=new GraphSurface();Check(!graph.NavigateNodes(new Vector(1,0)),"An empty board safely ignores navigation");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: spatial arrow-key selection, initial viewport anchor, directional preference, double rings, boundary behavior, automatic camera reveal, editor synchronization, typing/menu/modifier isolation, mixed selection, nested board isolation and unchanged documents.\n");
    }
}
