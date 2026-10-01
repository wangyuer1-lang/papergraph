using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Papergraph;

public static class MarkColorTests
{
    public static void Run()
    {
        void Check(bool pass,string reason){if(!pass)throw new Exception(reason);}
        var doc=new GraphDocument
        {
            Nodes=[new(){Id="a",X=-25,Y=90,Title="Review this claim",Note="Keep my notes"},new(){Id="b",X=375,Y=90,Title="Second claim"}],
            Edges=[new(){Id="ab",From="a",To="b",Note="Keep relation evidence",Direction="both"}],
            Regions=[new(){Id="frame",IsAbsolute=true,X=20,Y=50,Width=200,Height=220,Color="#579A88"},new(){Id="overlap",IsAbsolute=true,X=70,Y=90,Width=160,Height=180,Color="#777FB1"}]
        };
        var original=doc.Serialize();Check(!original.Contains("MarkColor"),"Unmarked documents keep their old serialization and revision");
        Check(GraphDocument.Parse(original).Nodes.All(n=>n.MarkColor==null),"Old documents load with automatic colors");
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-mark-colors-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,"marks.papergraph");Storage.Save(file,doc);File.WriteAllText(Path.Combine(directory,"recent.txt"),"marks.papergraph");
        var window=new MainWindow(directory);var graph=window.Graph;
        void Select(string id,bool edge=false){graph.ClearAllSelection();if(edge)graph.SelectedEdge=id;else graph.Selected=[id];window.SelectionChanged();}
        void Action(string label)=>window.EditMenu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()==label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        void Choose(string id,string name)
        {
            var menu=window.CreateColorMenu(window.ColorButton,id);var panel=(StackPanel)menu.Items[0];
            var buttons=panel.Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<Button>());
            buttons.Single(b=>AutomationProperties.GetName(b)==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        var locations=graph.Document.Nodes.Select(n=>(n.Id,n.X,n.Y,n.Title,n.Note,n.Color)).ToArray();
        Select("a");Choose("a","Mark Red");Check(graph.Document.Node("a")!.MarkColor=="#E5484D","Palette marks selected point");
        Select("ab",true);Check(window.ColorButton.Visibility==Visibility.Visible,"Relations expose the same color control");Choose("ab","Mark Blue");
        Check(graph.Document.Edges.Single().MarkColor=="#2F7DE1"&&graph.Document.Edges.Single().Note=="Keep relation evidence","Palette marks relation and preserves its notes");
        Check(locations.SequenceEqual(graph.Document.Nodes.Select(n=>(n.Id,n.X,n.Y,n.Title,n.Note,n.Color))),"Marks preserve existing colors, text, and layout");
        Action("Undo");Check(graph.Document.Edges.Single().MarkColor==null&&graph.Document.Node("a")!.MarkColor!=null,"Undo clears only the last mark");
        Action("Redo");Check(graph.Document.Edges.Single().MarkColor=="#2F7DE1","Redo restores relation color");

        var renderGraph=new GraphSurface{Document=graph.Document};renderGraph.Measure(new Size(600,320));renderGraph.Arrange(new Rect(0,0,600,320));renderGraph.SetView(1,new Point());
        Color Pixel(RenderTargetBitmap bitmap,int x,int y){var bytes=new byte[4];bitmap.CopyPixels(new Int32Rect(x,y,1,1),bytes,4,0);return Color.FromRgb(bytes[2],bytes[1],bytes[0]);}
        RenderTargetBitmap Render(){renderGraph.UpdateLayout();var bitmap=new RenderTargetBitmap(600,320,96,96,PixelFormats.Pbgra32);bitmap.Render(renderGraph);return bitmap;}
        foreach(bool dark in new[]{false,true})
        {
            graph.ApplyTheme(dark);renderGraph.ApplyTheme(dark);renderGraph.SelectedEdge="ab";renderGraph.RefreshSelection();var bitmap=Render();
            Check(Pixel(bitmap,100,150)==GraphMarkColors.Display("#E5484D",dark),$"Actual node fill keeps its mark inside overlapping frames in both themes: {Pixel(bitmap,100,150)} versus {GraphMarkColors.Display("#E5484D",dark)}");
            Check(Pixel(bitmap,300,150)==GraphMarkColors.Display("#2F7DE1",dark),"Selected relation shaft keeps its manual color in both themes");
            foreach(var (_,color) in GraphMarkColors.Palette)
                Check(GraphStyle.Contrast(GraphMarkColors.Display(color,dark),(Color)ColorConverter.ConvertFromString(dark?"#232730":"#F7F8FA"))>=3,"Palette is visible on both themes");
            var a=graph.Document.Node("a")!;a.X+=900;graph.RefreshData();Check(GraphMarkColors.Node(graph.Document,a,dark)==GraphMarkColors.Display(a.MarkColor!,dark),"Moving out of a frame retains the mark");a.X-=900;graph.RefreshData();
            graph.ConnectionDisplay=ConnectionDisplay.WithinFrames;Check(!graph.DrawnEdgeIds.Contains("ab"),"Mark does not reveal a filtered connection");
            graph.ConnectionDisplay=ConnectionDisplay.AcrossFrames;Check(graph.DrawnEdgeIds.Contains("ab"),"Across restores marked relation");
            graph.SetView(.05,new Point());Check(graph.Document.Edges.Single().MarkColor=="#2F7DE1"&&graph.Document.Node("a")!.MarkColor=="#E5484D","Overview zoom preserves marks");
            graph.SetView(1,new Point());graph.ConnectionDisplay=ConnectionDisplay.All;
        }
        Select("a");Choose("a","Clear mark color");Check(graph.Document.Node("a")!.MarkColor==null,"Automatic clears node mark");
        Check(GraphMarkColors.Node(graph.Document,graph.Document.Node("a")!,false)==GraphStyle.NodeColor(GraphRegionColors.ForNode(graph.Document.Node("a")!,graph.Document.Regions,graph.Document),false),"Clearing restores current intersection color");
        Select("ab",true);Choose("ab","Clear mark color");Check(graph.Document.Serialize()==original,"Clearing all marks returns exactly to the original document");
        Choose("a","Mark Purple");Choose("ab","Mark Orange");var marked=graph.Document.Serialize();window.Close();
        Check(GraphDocument.Parse(File.ReadAllText(file)).Serialize()==marked,"Mark colors save to disk");
        window=new MainWindow(directory);Check(window.Graph.Document.Serialize()==marked,"Mark colors survive restart");window.Close();

        var request=new JsonObject{["operation"]="editGraph",["requestId"]=Guid.NewGuid().ToString(),["expectedDocument"]=file,["expectedRevision"]=AgentProtocol.Revision(doc),
            ["nodes"]=new JsonArray(new JsonObject{["id"]="a",["markColor"]="#168A57"}),["edges"]=new JsonArray(new JsonObject{["id"]="ab",["markColor"]="#A052D8"})};
        GraphDocument Prepare(){using var j=JsonDocument.Parse(request.ToJsonString());return AgentGraphEdit.Prepare(doc,file,j.RootElement);}
        var edited=Prepare();Check(edited.Node("a")!.MarkColor=="#168A57"&&edited.Edges.Single().MarkColor=="#A052D8","Agent can read and write independent marks");
        request["edges"]![0]!["markColor"]="invalid";bool rejected=false;try{Prepare();}catch(InvalidDataException){rejected=true;}
        Check(rejected&&doc.Serialize()==original,"Invalid marks reject the whole batch without changing the source");
        var ring=new Proposition{Kind="circle",MarkColor="#E5484D",Color="#777FB1"};Check(GraphMarkColors.Node(doc,ring,true)==GraphMarkColors.Display("#E5484D",true),"Ring marks keep the same bright palette");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: manual node/ring/relation marks, real pixels in light/dark and selection, overlapping frame override, automatic reset, filtered/overview views, palette controls, undo/redo, legacy revisions, disk/restart persistence and atomic agent validation.\n");
    }
}
