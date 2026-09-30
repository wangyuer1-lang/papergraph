using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Papergraph;

public static class ConnectionTests
{
    public static void Run()
    {
        void Check(bool pass,string why){if(!pass)throw new Exception(why);}
        Proposition Point(string id,double x,double y,string? parent=null)=>new(){Id=id,Title=id,X=x-125,Y=y-60,Parent=parent};
        Relation Link(string id,string a,string b)=>new(){Id=id,From=a,To=b,Caption=id};
        var doc=new GraphDocument
        {
            Nodes=[Point("a",100,100),Point("b",250,100),Point("overlap",300,100),Point("c",650,100),Point("d",650,400),Point("e",800,400),
                new(){Id="ring",Kind="circle",Expanded=true},new(){Id="nested",Kind="circle",Expanded=true,Parent="ring"},Point("r1",130,260,"nested"),Point("r2",180,260,"nested")],
            Edges=[Link("ab","a","b"),Link("bi","b","overlap"),Link("ic","overlap","c"),Link("ac","a","c"),Link("ad","a","d"),Link("de","d","e"),Link("rc","r1","c"),Link("gb","ring","b"),Link("rr","r1","r2")],
            Regions=[new(){Id="A",IsAbsolute=true,X=50,Y=50,Width=300,Height=300},new(){Id="B",IsAbsolute=true,X=280,Y=50,Width=450,Height=150},
                new(){Id="inside-ring",IsAbsolute=true,Parent="nested",X=110,Y=230,Width=90,Height=70}]
        };
        doc.Validate();var original=doc.Serialize();
        Check(GraphConnections.MatchingIds(doc,ConnectionDisplay.WithinFrames).SetEquals(["ab","bi","ic","gb","rr"]),"Common frames, overlapping frames and nested ring contents count as within-frame links");
        Check(GraphConnections.MatchingIds(doc,ConnectionDisplay.AcrossFrames).SetEquals(["ac","ad","rc"]),"Across includes different frames and frame-to-outside, but not wholly unframed links");
        Check(GraphConnections.MatchingIds(doc,ConnectionDisplay.All).Count==9,"All includes unframed links");
        var parent=new Region{Id="outer",IsAbsolute=true,X=0,Y=0,Width=750,Height=350};doc.Regions.Add(parent);
        Check(GraphConnections.MatchingIds(doc,ConnectionDisplay.WithinFrames).Contains("ac"),"A common outer frame is still a shared frame");doc.Regions.Remove(parent);

        var graph=new GraphSurface{Document=doc,ShowCaptions=true};graph.Measure(new Size(900,650));graph.Arrange(new Rect(0,0,900,650));graph.SetView(1,new Point());
        var allLinks=graph.VisibleLinks.ToArray();var view=(graph.Zoom,graph.Offset);var notifications=0;graph.SelectionChanged+=()=>notifications++;
        graph.SelectedEdge="ad";graph.ConnectionDisplay=ConnectionDisplay.WithinFrames;
        Check(graph.SelectedEdge==null&&notifications==1,"Hiding a selected link clears its inspector selection");
        Check(graph.DrawnEdgeIds.ToHashSet().SetEquals(["ab","bi","ic","gb","rr"]),"Only matching geometry is rendered");
        Check(graph.HitRelation(new Point(375,250))==null,"A hidden cross-frame shaft cannot be selected");
        Check(!graph.LayoutCaptions().Any(p=>p.IsEdge&&p.Id=="ad"),"Hidden link titles are removed from layout and hit testing");
        graph.Selected=["a"];graph.ConnectionDisplay=ConnectionDisplay.AcrossFrames;
        Check(graph.DrawnEdgeIds.ToHashSet().SetEquals(["ac","ad","rc"])&&graph.Selected.SetEquals(["a"]),"Across mode keeps point selection and only draws cross-frame links");
        Check(graph.HitRelation(new Point(375,250))=="ad","Visible cross-frame links remain selectable");
        Check(graph.LayoutCaptions().Any(p=>p.IsEdge&&p.Id=="ad"),"Visible link titles return with their edges");
        Check(graph.VisibleLinks.SequenceEqual(allLinks),"Display filters do not remove relationships from layout or drag constraints");
        Check((graph.Zoom,graph.Offset)==view&&doc.Serialize()==original,"Filtering preserves camera and all graph content");
        graph.SetView(.02,new Point());Check(graph.DrawnEdgeIds.ToHashSet().SetEquals(["ac","ad","rc"]),"Frame classification does not change at overview zoom");
        graph.SetView(1,new Point());graph.ConnectionDisplay=ConnectionDisplay.WithinFrames;
        doc.Regions[1].X+=1000;graph.ContentChanged();Check(!graph.DrawnEdgeIds.Contains("ic"),"Moving a frame immediately updates spatial classification");
        doc.Regions[1].X-=1000;graph.RefreshData();Check(graph.DrawnEdgeIds.Contains("ic"),"Refreshing after move or undo restores membership");
        graph.SetBoard(null,"A");graph.ConnectionDisplay=ConnectionDisplay.AcrossFrames;Check(graph.DrawnEdgeIds.Count==0,"Frame board filtering never reveals outside objects");
        graph.ConnectionDisplay=ConnectionDisplay.WithinFrames;Check(graph.DrawnEdgeIds.Contains("rr"),"Within mode still works after entering a frame");
        graph.SetBoard("nested",null);Check(graph.DrawnEdgeIds.SequenceEqual(["rr"]),"Entered rings retain their inherited frame membership");
        graph.SetBoard(null,null);graph.ConnectionDisplay=ConnectionDisplay.All;Check(graph.DrawnEdgeIds.Count==9&&doc.Serialize()==original,"All restores every link without changing data");

        var directory=Path.Combine(Path.GetTempPath(),"papergraph-connections-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,"graph.papergraph");Storage.Save(file,doc);File.WriteAllText(Path.Combine(directory,"recent.txt"),"graph.papergraph");
        File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"dark\":true,\"unrelated\":\"keep\"}");
        var window=new MainWindow(directory);
        Check(window.ConnectionButton.Content?.ToString()=="Links: All"&&window.Graph.Dark,"Old settings load All without losing dark theme");
        window.ConnectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(window.Graph.ConnectionDisplay==ConnectionDisplay.WithinFrames&&window.ConnectionButton.Content?.ToString()=="Links: Within","Button cycles to Within");
        window.ConnectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(window.Graph.ConnectionDisplay==ConnectionDisplay.AcrossFrames,"Button cycles to Across");
        window.ConnectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(window.Graph.ConnectionDisplay==ConnectionDisplay.All,"Button cycles back to All");
        var menu=window.ViewMenu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()=="Connections");
        menu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()=="Across □").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(window.Graph.ConnectionDisplay==ConnectionDisplay.AcrossFrames&&window.ConnectionButton.Content?.ToString()=="Links: Across","Menu directly selects a mode and synchronizes button");
        window.ThemeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.Close();
        var restored=new MainWindow(directory);
        Check(restored.Graph.ConnectionDisplay==ConnectionDisplay.AcrossFrames&&!restored.Graph.Dark,"Connection preference and theme both survive restart");
        Check(File.ReadAllText(Path.Combine(directory,"settings.json")).Contains("keep"),"Saving view settings preserves unrelated preferences");
        restored.Close();Check(GraphDocument.Parse(File.ReadAllText(file)).Serialize()==original,"View preferences never rewrite graph content");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: connection display modes, overlap and ring membership, dynamic frame movement, hidden shaft/title hit testing, board scope, unchanged content/layout constraints, toolbar/menu and preference persistence.\n");
    }
}
