using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Papergraph;

public static class CaptionTests
{
    public static void Run()
    {
        void Check(bool pass,string reason){if(!pass)throw new Exception(reason);}
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-titles-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,"titles.papergraph");
        var doc=new GraphDocument{Nodes=[new(){Id="a",X=75,Y=140,Title="Full proposition",Note="Supporting notes"},new(){Id="b",X=395,Y=140,Title="Another proposition"}],Edges=[new(){Id="edge",From="a",To="b",Note="Relation notes"}]};
        Storage.Save(file,doc);File.WriteAllText(Path.Combine(directory,"recent.txt"),"titles.papergraph");
        var window=new MainWindow(directory);var graph=window.Graph;
        Check(!graph.ShowCaptions,"Titles start hidden so the first Space shows them");var untouched=graph.Document.Serialize();
        Check(window.RouteTitleToggle(Key.Space,ModifierKeys.None,graph)&&graph.ShowCaptions,"One Space shows all titles");
        window.RouteTitleToggle(Key.Space,ModifierKeys.None,graph,true);Check(graph.ShowCaptions,"Holding Space does not toggle repeatedly");
        foreach(var editor in new[]{window.CaptionBox,window.PropositionBox,window.NotesBox,window.DocumentTitleBox})Check(!window.RouteTitleToggle(Key.Space,ModifierKeys.None,editor),"Space remains ordinary text in all editors");
        Check(!window.RouteTitleToggle(Key.Space,ModifierKeys.None,new MenuItem())&&!window.RouteTitleToggle(Key.Space,ModifierKeys.Control,graph),"Menus and modified chords are not intercepted");
        window.RouteTitleToggle(Key.Space,ModifierKeys.None,graph);Check(!graph.ShowCaptions&&graph.Document.Serialize()==untouched,"Second Space hides titles without changing the document");
        void Select(string id){graph.ClearAllSelection();if(graph.Document.Node(id)!=null)graph.Selected=[id];else graph.SelectedEdge=id;window.SelectionChanged();}
        void Action(string name)=>window.EditMenu.Items.OfType<MenuItem>().Single(m=>(string)m.Header==name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Select("a");var original=graph.Document.Node("a")!;var radius=GraphStyle.Radius(original);var center=GraphStyle.Center(original);
        Check(window.CaptionPanel.Visibility==Visibility.Visible&&window.CaptionBox.Text=="","Old points expose an empty optional title");
        window.CaptionBox.Text="Evidence 证据";
        Check(original.Caption=="Evidence 证据"&&original.Title=="Full proposition"&&original.Note=="Supporting notes"&&GraphStyle.Radius(original)==radius&&GraphStyle.Center(original)==center,"Graph title leaves proposition, notes and geometry intact");
        Action("Undo");Select("a");Check(window.CaptionBox.Text=="","Title edit can be undone");Action("Redo");Select("a");Check(window.CaptionBox.Text=="Evidence 证据","Title redo restores Unicode text");
        Select("edge");Check(window.CaptionBox.Text==""&&window.NotesBox.Text=="Relation notes","Relations have an independent title and notes");window.CaptionBox.Text="Implies";
        window.DirectionChoices.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.ArrowChoices.Children.OfType<Button>().ElementAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(window.CaptionBox.Text=="Implies"&&graph.Document.Edges.Single().Caption=="Implies","Arrow changes preserve the title");
        Select("a");Check(window.CaptionBox.Text=="Evidence 证据","Selection restores the correct point title");window.Close();
        var saved=GraphDocument.Parse(File.ReadAllText(file));Check(saved.Nodes[0].Caption=="Evidence 证据"&&saved.Edges[0].Caption=="Implies","Titles survive disk save");
        window=new MainWindow(directory);Check(window.Graph.Document.Serialize()==saved.Serialize(),"Titles survive restart");window.Close();
        Check(saved.Markdown().Contains("## Evidence 证据")&&saved.Markdown().Contains("Full proposition")&&saved.Markdown().Contains("Implies: ")&&saved.Markdown().Contains("Relation notes"),"Export preserves titles and full writing");
        var legacy=System.Text.Json.Nodes.JsonNode.Parse(saved.Serialize())!;foreach(var n in legacy["Nodes"]!.AsArray())n!.AsObject().Remove("Caption");foreach(var e in legacy["Edges"]!.AsArray())e!.AsObject().Remove("Caption");var loaded=GraphDocument.Parse(legacy.ToJsonString());Check(loaded.Nodes.All(n=>n.Caption=="")&&loaded.Edges.All(e=>e.Caption==""),"Missing title fields remain backward compatible");
        var circle=saved.Collapse(["a","b"],null);circle.Caption="Argument";saved.Dissolve(circle.Id);Check(saved.Node(circle.Id)?.Caption=="Argument","Ungrouping preserves a title-only double ring");

        var measured=GraphCaptions.Measure("Evidence",Brushes.Black,1);var size=new Size(measured.Width,measured.Height);
        var vertical=GraphCaptions.Edge("e",size,new Point(350,200),new Vector(0,1),.5);var textCenter=new Point(vertical.TextBounds.X+vertical.TextBounds.Width/2,vertical.TextBounds.Y+vertical.TextBounds.Height/2);Check(Math.Abs(vertical.Angle)<=90&&vertical.Contains(vertical.Transform.Transform(textCenter)),"Scaled vertical relation titles stay upright and clickable");
        graph=new GraphSurface{Document=loaded,ShowCaptions=true};graph.Document.Nodes[0].Caption="Evidence";graph.Document.Nodes[1].Caption="Claim";graph.Document.Edges[0].Caption="Implies";
        graph.Measure(new Size(1800,1200));graph.Arrange(new Rect(0,0,1800,1200));var snapshot=graph.Document.Serialize();
        foreach(var zoom in new[]{.1,.25,.5,1,2,3.2})
        {
            graph.SetView(zoom,new Point(50,50));var visible=graph.LayoutCaptions();Check(visible.Count==3,"All titles remain present at every zoom, including short edges");
            var expectedScale=Math.Max(1,zoom);var title=visible.Single(p=>p.Id=="a");Check(Math.Abs(title.Bounds.Width-measured.Width*expectedScale)<.001&&Math.Abs(title.Bounds.Height-measured.Height*expectedScale)<.001,"Titles stay at least 16 screen pixels and grow when zoomed in");
            Check(visible.Single(p=>p.IsEdge).Scale==expectedScale,"Relation titles share the same readable minimum size");
            var node=graph.Document.Node("a")!;var screenRadius=GraphStyle.DisplayRadius(GraphStyle.Radius(node),zoom)*zoom;var screenCenter=graph.ToScreen(GraphStyle.Center(node));var nearest=new Point(Math.Clamp(screenCenter.X,title.Bounds.Left,title.Bounds.Right),Math.Clamp(screenCenter.Y,title.Bounds.Top,title.Bounds.Bottom));var distance=(nearest-screenCenter).Length;Check(distance>=screenRadius+7.9*expectedScale&&distance<=screenRadius+46*expectedScale,"Repositioned titles stay near their point at overview zooms");
            var hit=new Point(title.Bounds.X+title.Bounds.Width/2,title.Bounds.Y+title.Bounds.Height/2);Check(graph.HitNode(graph.ToWorld(hit))?.Id=="a","Scaled point-title hit targets follow their text");
        }
        graph.SetView(1,new Point(0,0));var priorTitle=graph.LayoutCaptions().Single(p=>p.Id=="a");var titleHit=new Point(priorTitle.Bounds.X+priorTitle.Bounds.Width/2,priorTitle.Bounds.Y+priorTitle.Bounds.Height/2);
        graph.ShowCaptions=false;Check(graph.LayoutCaptions().Count==0&&graph.HitNode(graph.ToWorld(titleHit))==null,"Hidden titles disappear from both rendering and hit testing");graph.ShowCaptions=true;
        Check(graph.Document.Serialize()==snapshot,"Zooming and toggling never change saved content");
        graph.Document.Nodes[0].Caption=new string('W',400);graph.ContentChanged();Check(graph.LayoutCaptions().Any(p=>p.Id=="a"),"A long title clips at the canvas edge instead of vanishing");graph.Document.Nodes[0].Caption="Evidence";graph.ContentChanged();
        graph.ApplyTheme(true);Check(graph.LayoutCaptions().Count==3,"Dark theme retains all titles");
        graph.Document.Nodes[1].X=graph.Document.Nodes[0].X;graph.Document.Nodes[1].Y=graph.Document.Nodes[0].Y+40;graph.ContentChanged();Check(graph.LayoutCaptions().Count==3,"Crowding no longer hides point or edge titles");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: independent titles, unchanged point radii, undo/redo, Unicode persistence and legacy compatibility, lossless export, 16-pixel minimum at all zooms with anchored hit targets, no automatic hiding, Space show/hide without repeat, editor and menu isolation.\n");
    }
}
