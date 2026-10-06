using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Papergraph;

public static class FrameNavigationTests
{
    public static void Run()
    {
        void Check(bool pass,string message){if(!pass)throw new Exception(message);}
        Proposition Node(string id,double x,double y,string? parent=null)=>new(){Id=id,Title=id,Parent=parent,X=x-125,Y=y-60};
        Region Frame(string id,double x,double y,double width=300,double height=300)=>new(){Id=id,Title=id,IsAbsolute=true,X=x,Y=y,Width=width,Height=height};
        var doc=new GraphDocument
        {
            Nodes=[Node("a",450,450),Node("a-up",450,350),Node("a-down",450,550),Node("a-left",350,450),Node("a-right",550,450),
                new(){Id="top-ring",Kind="circle",Expanded=true},Node("b",450,150,"top-ring"),Node("b-high",450,70,"top-ring"),
                Node("c",450,750),Node("left",100,450),Node("right",800,450),Node("diagonal",670,170)],
            Regions=[Frame("a-frame",300,300),Frame("b-frame",300,0,300,220),Frame("c-frame",300,700),Frame("left-frame",0,300,200,300),
                Frame("right-frame",700,300),Frame("diagonal-frame",640,40,90,200),Frame("empty",1100,300,200,300),Frame("wrapper",-40,-40,1400,1100)],
            Edges=[new(){Id="ring-edge",From="b",To="b-high"}]
        };
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-frame-navigation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,"navigation.papergraph");Storage.Save(file,doc);File.WriteAllText(Path.Combine(directory,"recent.txt"),"navigation.papergraph");
        var window=new MainWindow(directory);var graph=window.Graph;graph.Measure(new Size(900,650));graph.Arrange(new Rect(0,0,900,650));graph.SetView(1,new Point());
        var original=graph.Document.Serialize();
        void Select(string id){window.ResetFrameTap();graph.ClearAllSelection();graph.Selected=[id];window.SelectionChanged();}
        void Press(Key key,int time,bool repeat=false)=>Check(window.RouteArrowKey(key,ModifierKeys.None,graph,repeat,time),"Canvas arrows are handled");
        foreach(var (key,local,destination) in new[]{(Key.Up,"a-up","b"),(Key.Down,"a-down","c"),(Key.Left,"a-left","left"),(Key.Right,"a-right","right")})
        {
            Select("a");Press(key,1000);Check(graph.Selected.SetEquals([local]),"Single arrow stays in current frame");
            Press(key,1180);Check(graph.Selected.SetEquals([destination]),"Double arrow enters nearest frame in that direction: "+key);
            Check(graph.BoardRegion==null&&window.PropositionBox.Text==destination,"Overview jump preserves overview and updates the editor");
        }
        Select("a");Press(Key.Up,2000);Press(Key.Up,2400);Check(graph.Selected.SetEquals(["a-up"]),"Slow repeated arrows do not jump");
        Select("a");Press(Key.Up,3000);Press(Key.Right,3100);Check(GraphBoard.Bounds(graph.Document.Frame("a-frame")!).Contains(GraphStyle.Center(graph.Document.Node(graph.Selected.Single())!)),"Mixed directions are not a double tap");
        Select("a");Press(Key.Up,4000);Press(Key.Up,4100,true);Press(Key.Up,4160,true);Press(Key.Up,4200);Check(graph.Selected.SetEquals(["a-up"]),"Holding and releasing a key does not become a double tap");
        Select("a");Press(Key.Up,5000);window.ResetFrameTap();Press(Key.Up,5100);Check(graph.Selected.SetEquals(["a-up"]),"Mouse interaction resets the gesture");
        Select("a");Press(Key.Up,6000);graph.Selected=["a-right"];window.SelectionChanged();Press(Key.Up,6100);Check(!graph.Selected.Contains("b"),"Changing selection cancels the previous tap");
        foreach(var control in new IInputElement[]{window.PropositionBox,window.NotesBox,window.CaptionBox,window.DocumentTitleBox,window.NotePage2,new MenuItem(),new ContextMenu()})
        {
            Select("a");Press(Key.Up,7000);Check(!window.RouteArrowKey(Key.Up,ModifierKeys.None,control,false,7100),"Editors, note tabs and menus retain their arrows");Press(Key.Up,7200);Check(graph.Selected.SetEquals(["a-up"]),"Returning from another control starts a new gesture");
        }
        Select("a");Press(Key.Up,8000);Check(!window.RouteArrowKey(Key.Up,ModifierKeys.Shift,graph,false,8100),"Modified arrows never jump");Press(Key.Up,8200);Check(graph.Selected.SetEquals(["a-up"]),"Modifier clears the gesture");
        Select("right");Press(Key.Right,9000);Press(Key.Right,9200);Check(graph.SelectedRegion=="empty"&&graph.Selected.Count==0,"An empty neighbor frame can be reached");
        Press(Key.Left,10000);Press(Key.Left,10200);Check(graph.Selected.SetEquals(["right"]),"Can jump back from an empty frame");
        Select("b");Press(Key.Up,11000);Press(Key.Up,11200);Check(graph.Selected.SetEquals(["b-high"]),"No neighboring frame means no wrap and no jump into wrapper");
        Select("a");window.EnterRegion("a-frame");Select("a");Press(Key.Up,12000);Press(Key.Up,12200);
        Check(graph.BoardRegion=="b-frame"&&graph.Selected.SetEquals(["b"]),"A frame board can switch to an off-board neighbor and enter a ring member");
        window.Leave();Check(graph.BoardRegion=="a-frame","Back returns to previous frame board after a jump");window.Leave();
        Select("a");Press(Key.Up,int.MaxValue-100);Press(Key.Up,unchecked(int.MinValue+50));Check(graph.Selected.SetEquals(["b"]),"Timestamp rollover still recognizes a double tap");
        for(int i=0;i<90;i++)graph.AdvanceView(1d/60);
        var pos=graph.ToScreen(GraphStyle.Center(graph.Document.Node("b")!));Check(pos.X>=69&&pos.X<=graph.ActualWidth-69&&pos.Y>=69&&pos.Y<=graph.ActualHeight-69,"Jump reveals the target on screen");
        Check(graph.Document.Serialize()==original,"Frame navigation never changes propositions, notes, geometry or relations");window.Close();Check(GraphDocument.Parse(File.ReadAllText(file)).Serialize()==original,"Closing after navigation preserves the document exactly");
        graph=new GraphSurface{Document=new GraphDocument{Nodes=[Node("bottom",150,600),Node("top",150,150)],Regions=[Frame("outer",0,0,600,900),Frame("lower",50,450,250,300),Frame("upper",50,50,250,300)]}};
        graph.Selected=["bottom"];var nested=graph.FindFrameJump(new Vector(0,-1));Check(nested?.Frame.Id=="upper"&&nested.NodeId=="top","Nested frames jump to a sibling rather than their enclosing wrapper");
        graph=new GraphSurface{Document=new GraphDocument{Nodes=[Node("first",100,100),Node("overlap",250,100),Node("next",450,100)],Regions=[Frame("first-frame",0,0,300,300),Frame("next-frame",200,0,300,300)]}};
        graph.Selected=["first"];graph.NavigateNodes(new Vector(1,0));var overlap=graph.FindFrameJump(new Vector(1,0));Check(overlap?.Frame.Id=="next-frame"&&overlap.NodeId=="next","Intentional overlap crossing chooses a new point in the neighboring frame");
        graph.SelectFrameJump(overlap!);Check(!graph.NavigateNodes(new Vector(1,0))&&graph.Selected.SetEquals(["next"]),"Subsequent single arrows remain in the new frame");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: directional double-tap frame jumps, single/held-key isolation, 350 ms timing and rollover, nearest frame, empty frames, ring members, board/Back navigation, focus/modifier/mouse guards and unchanged saved content.\n");
    }
}
