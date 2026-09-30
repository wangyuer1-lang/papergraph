using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Papergraph;

public static class FitTests
{
    public static void Run()
    {
        void Check(bool pass,string reason){if(!pass)throw new Exception(reason);}
        Proposition Point(string id,double x,double y,string? parent=null)=>new(){Id=id,Title=new string('x',200),X=x-125,Y=y-60,Parent=parent};
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-fit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var doc=new GraphDocument
        {
            Nodes=[Point("left",-12000,-5000),new(){Id="outer",Kind="circle",Expanded=true},new(){Id="inner",Kind="circle",Expanded=true,Parent="outer"},Point("a",16000,24000,"inner"),Point("b",18000,24200,"inner"),Point("c",22000,25500,"outer")],
            Edges=[new(){From="a",To="b"},new(){From="left",To="outer"},new(){From="left",To="outer",Label="Association"}],
            Regions=[new(){Id="empty",IsAbsolute=true,X=-16000,Y=10000,Width=6000,Height=10000},new(){Id="nested",IsAbsolute=true,Parent="outer",X=15000,Y=23000,Width=5000,Height=2500}]
        };
        var file=Path.Combine(directory,"fit.papergraph");Storage.Save(file,doc);File.WriteAllText(Path.Combine(directory,"recent.txt"),"fit.papergraph");
        var window=new MainWindow(directory);var graph=window.Graph;
        graph.Measure(new Size(900,650));graph.Arrange(new Rect(0,0,900,650));graph.RightInset=100;
        var original=graph.Document.Serialize();
        void Settle(){for(int i=0;i<180;i++)graph.AdvanceView(1d/60);}
        void CheckFit()
        {
            var bounds=graph.VisibleNodes.Select(graph.ObjectBounds).Concat(graph.VisibleRegions.Select(graph.RegionBounds)).ToList();
            if(graph.BoardBounds is Rect board)bounds.Add(board);
            foreach(var b in bounds)
            {
                var a=graph.ToScreen(b.TopLeft);var z=graph.ToScreen(b.BottomRight);
                Check(a.X>=89.9&&a.Y>=79.9&&z.X<=710.1&&z.Y<=570.1,"Full fit encloses every visible point, nested ring and frame with padding, outside the writing panel");
            }
        }
        graph.SetView(3.2,new Point(100,-100));graph.Fit(false);Check(graph.Zoom<.10,"A large graph can fit below the former 10% floor");CheckFit();
        var full=(graph.Zoom,graph.Offset);graph.Fit(false);Check(graph.Zoom==full.Zoom&&(graph.Offset-full.Offset).Length<.001,"Repeated fit is stable");
        graph.SetView(.001,new Point(-8000,4000));graph.Fit(false);Check(Math.Abs(graph.Zoom-full.Zoom)<1e-12&&(graph.Offset-full.Offset).Length<.001,"Fit is independent of the starting zoom and pan");
        window.FitEditButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Settle();Check(graph.Zoom==.10,"Editing toolbar fit retains the original minimum scale");
        window.FitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Settle();CheckFit();
        Check(window.RouteFit(Key.F,ModifierKeys.Shift,graph),"Shift+F invokes editing fit");Settle();Check(graph.Zoom==.10,"Editing shortcut retains the original minimum scale");
        var anchor=new Point(250,230);var world=graph.ToWorld(anchor);graph.ZoomBy(.25,anchor);Settle();
        Check(Math.Abs(graph.Zoom-.025)<1e-12&&(graph.ToScreen(world)-anchor).Length<.001,"Manual zoom passes below 10% while keeping the pointer fixed");
        graph.ZoomBy(4,anchor);Settle();Check(Math.Abs(graph.Zoom-.10)<1e-12&&(graph.ToScreen(world)-anchor).Length<.001,"Zoom reverses normally from overview scale");
        Check(window.RouteFit(Key.F,ModifierKeys.None,graph),"F invokes full fit");Settle();CheckFit();
        var before=(graph.Zoom,graph.Offset);
        foreach(var editor in new[]{window.PropositionBox,window.NotesBox,window.CaptionBox,window.DocumentTitleBox})
            Check(!window.RouteFit(Key.F,ModifierKeys.None,editor)&&!window.RouteFit(Key.F,ModifierKeys.Shift,editor),"Typing f or F in an editor never changes the camera");
        Check(!window.RouteFit(Key.F,ModifierKeys.Control,graph)&&!window.RouteFit(Key.F,ModifierKeys.Alt,graph)&&!window.RouteFit(Key.F,ModifierKeys.None,new MenuItem()),"Other shortcuts and menus do not trigger Fit");Settle();
        Check((graph.Zoom,graph.Offset)==before,"Rejected fit shortcuts preserve the camera");
        graph.SetBoard("outer",null);graph.Fit(false);CheckFit();Check(graph.VisibleNodes.All(n=>n.Id!="left")&&!graph.VisibleRegions.Any(r=>r.Id=="empty"),"Full fit respects an entered ring board");
        graph.SetBoard(null,"empty");graph.Fit(false);CheckFit();Check(graph.VisibleNodes.Count==0,"An empty frame board still fits its frame");
        graph.SetBoard(null,null);graph.Fit(false);CheckFit();Check(graph.Document.Serialize()==original,"Both fit modes and zoom preserve all content and positions");
        graph.SetView(GraphSurface.MinimumZoom,new Point(30,20));graph.ZoomBy(2,anchor);Settle();Check(graph.Zoom>GraphSurface.MinimumZoom,"Zoom can reverse at the new minimum");
        window.Close();Check(GraphDocument.Parse(File.ReadAllText(file)).Serialize()==original,"Camera operations do not change the saved graph");

        graph=new GraphSurface{Document=new GraphDocument()};graph.Measure(new Size(900,650));graph.Arrange(new Rect(0,0,900,650));graph.Fit(false);Check(graph.Zoom==1&&graph.Offset==new Point(450,325),"Empty graph has a useful finite view");
        graph.Document=new GraphDocument{Nodes=[Point("near",-1e8,-1e8),Point("far",1e8,1e8)]};graph.Fit(false);
        Check(graph.Zoom<.00001&&graph.ToScreen(GraphStyle.Center(graph.Document.Nodes[0])).X>0&&graph.ToScreen(GraphStyle.Center(graph.Document.Nodes[1])).Y<650,"Very large coordinates are not clipped by an overview zoom floor");
        graph.Document=new GraphDocument{Nodes=[Point("small",0,0),Point("other",200,0)]};graph.Fit(false);var small=(graph.Zoom,graph.Offset);graph.Fit(false,editing:true);
        Check((graph.Zoom,graph.Offset)==small&&graph.Zoom>.10,"Both modes use normal fit for a small graph");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: full/edit toolbar and shortcuts, large graph containment, nested rings and frames, stable fit, pointer zoom below 10%, board scope, editor isolation and unchanged saved content.\n");
    }
}
