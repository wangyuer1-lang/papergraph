using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Papergraph;

public static class FrameFeatureTests
{
    public static void Run()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var doc=new GraphDocument{Title="Frames",Nodes=[new(){Id="n",Title="Member",X=25,Y=90},new(){Id="p",Title="Outside",X=975,Y=90}],
            Regions=[new(){Id="a",Caption="Evidence",Title="Frame body",Note="Separate source notes",IsAbsolute=true,X=50,Y=50,Width=350,Height=300},new(){Id="b",Caption="Conclusion",Title="",IsAbsolute=true,X=650,Y=50,Width=350,Height=300}],
            Edges=[new(){Id="ab",From="a",To="b",Caption="Supports"},new(){Id="an",From="a",To="n"},new(){Id="pb",From="p",To="b",Direction="reverse"}]};
        doc.Validate();var parsed=GraphDocument.Parse(doc.Serialize());Check(parsed.Frame("a")!.Caption=="Evidence"&&parsed.Edges.Count==3,"Frame titles and endpoint IDs survive roundtrip");
        var graph=new GraphSurface{Document=doc,ShowCaptions=true};graph.Measure(new Size(1250,600));graph.Arrange(new Rect(0,0,1250,600));
        foreach(var zoom in new[]{.15,1d,2d})
        {
            graph.SetView(zoom,new Point());Check(graph.ConnectionEndpointTarget(graph.RegionPort(doc.Frame("a")!),"p")=="a","Frame port accepts connections at all zooms");
            Check(graph.ConnectionEndpointTarget(new Point(400,300),"p")=="a","Frame border accepts connections");
            Check(graph.ConnectionEndpointTarget(GraphStyle.Center(doc.Node("n")!),"p")=="n","Frame membership does not steal a node endpoint");
            Check(graph.ConnectionEndpointTarget(graph.RegionPort(doc.Frame("a")!),"a")==null,"No self-connections");
            Check(graph.DrawnEdgeIds.Count==3,"All frame relations are rendered");
            Check(graph.ContentBounds(zoom).Width>=1050,"Fit includes frame endpoints");
        }
        graph.SetView(1,new Point());Check(graph.HitRelation(new Point(525,200))=="ab","Frame-to-frame line is hit-testable between borders");
        Check(graph.EndpointAnchor("a",new Point(800,200)).X>400&&graph.EndpointAnchor("b",new Point(200,200)).X<650,"Frame arrows attach to facing borders");
        Check(graph.LayoutCaptions().Any(c=>c.Id=="a"&&!c.IsEdge),"Frame title is visible and independently hit-testable");
        graph.ConnectionDisplay=ConnectionDisplay.WithinFrames;Check(graph.DrawnEdgeIds.SequenceEqual(["an"]),"Frame to own member is Within");
        graph.ConnectionDisplay=ConnectionDisplay.AcrossFrames;Check(graph.DrawnEdgeIds.ToHashSet().SetEquals(["ab","pb"]),"Frame-to-frame and frame-to-outside are Across");
        var fragment=GraphClipboard.Capture(doc,[],["a","b"]);Check(fragment.Graph.Edges.Select(e=>e.Id).ToHashSet().SetEquals(["ab","an"]),"Copy includes frame relations only when both endpoints are copied");
        var pasted=GraphClipboard.PreparePaste(new(),fragment,new Point(0,0),null,null).Document;pasted.Validate();Check(pasted.Edges.Count==2&&pasted.Regions.Single(r=>r.Caption=="Evidence").Title=="Frame body","Paste remaps frame endpoints and keeps body/notes");
        var export=doc.Markdown();Check(export.Contains("Evidence [Support] → Conclusion")&&export.Contains("Frame body"),"Export includes frame relations and independent text");
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-frame-features-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var path=Path.Combine(directory,"Example.papergraph");Storage.Save(path,doc);
        var window=new MainWindow(directory);window.Graph.SelectedRegion="a";window.SelectionChanged();
        Check(window.CaptionPanel.Visibility==Visibility.Visible&&window.LinkButton.Visibility==Visibility.Visible&&window.PropositionBox.Text=="Frame body","Frame inspector exposes title, body and Connect");
        window.CaptionBox.Text="Evidence revised";window.PropositionBox.Text="正文独立";window.NotesBox.Text="Notes independent";
        Check(window.Graph.Document.Frame("a")!.Caption=="Evidence revised"&&window.Graph.Document.Frame("a")!.Title=="正文独立","Frame text editors are independent");
        void Invoke(string method)=>typeof(MainWindow).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);
        Invoke("DeleteSelection");Check(window.Graph.Document.Frame("a")==null&&window.Graph.Document.Node("n")!=null&&window.Graph.Document.Edges.Count==1,"Deleting a frame keeps its contents and removes attached edges");
        Invoke("Undo");Check(window.Graph.Document.Edges.Count==3&&window.Graph.Document.Frame("a")!.Note=="Notes independent","Undo restores the frame, text and edges together");
        JsonElement Read(object request)=>JsonSerializer.SerializeToElement(window.HandleAgent(JsonSerializer.SerializeToElement(request)));
        var snap=Read(new{operation="snapshot"});
        Read(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=path,expectedRevision=snap.GetProperty("revision").GetString(),regions=new[]{new{id="b",caption="Agent frame",body="Agent body",note="Agent note"}},edges=new[]{new{id="ba",from="b",to="a",kind="Inference",direction="both"}}});
        var found=Read(new{operation="search",query="Agent body"});Check(found.GetProperty("regions").GetArrayLength()==1&&found.GetProperty("regions")[0].GetProperty("caption").GetString()=="Agent frame","Agent searches frame body and exposes title independently");
        var before=window.Graph.Document.Serialize();bool rejected=false;try{Read(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=path,expectedRevision=AgentProtocol.Revision(window.Graph.Document),edges=new[]{new{id="bad",from="a",to="missing"}}});}catch{rejected=true;}Check(rejected&&window.Graph.Document.Serialize()==before,"Invalid frame endpoint batch is atomic");
        // Exercise real WPF render and save a visual fixture in the test output folder.
        window.Graph.Measure(new Size(1250,600));window.Graph.Arrange(new Rect(0,0,1250,600));window.Graph.SetView(1,new Point());window.Graph.ShowCaptions=true;window.Graph.SelectedRegion="a";
        var bitmap=new RenderTargetBitmap(1250,600,96,96,PixelFormats.Pbgra32);bitmap.Render(window.Graph);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"frame-features-preview.png")))encoder.Save(output);
        window.Close();window=new MainWindow(directory);Check(window.Graph.Document.Frame("b")!.Title=="Agent body"&&window.Graph.Document.Edges.Any(e=>e.From=="b"&&e.To=="a"),"Restart preserves frame writing and connections");window.Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: frame endpoints, rectangle anchors, visibility filters, text fields, copy/paste, deletion/undo, export, agent atomic edits/search, WPF render and restart.\n");
    }
}
