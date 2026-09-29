using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace Papergraph;

public static class EditorTests
{
    public static void Run()
    {
        void Check(bool pass,string reason){if(!pass)throw new Exception(reason);}
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-editor-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var legacyFile=Path.Combine(directory,"legacy.yujian");
        var doc=new GraphDocument{Title="Existing paper",Nodes=[new(){Id="a",X=-25,Y=40,Title="Existing claim",Note="Existing notes"},new(){Id="b",X=225,Y=40,Title="Other claim",Note="Other notes"},new(){Id="group",X=522,Y=22,Kind="circle",Expanded=true,Title="Group claim",Note="Group notes"}],Regions=[new(){Id="frame",IsAbsolute=true,X=800,Y=50,Width=140,Height=160,Title="Frame claim",Note="Frame notes"}],Edges=[new(){From="a",To="b",Label="\u652f\u6301"}]};
        Storage.Save(legacyFile,doc);File.WriteAllText(Path.Combine(directory,"\u6700\u8fd1\u6253\u5f00.txt"),"legacy.yujian");File.WriteAllText(Path.Combine(directory,"\u754c\u9762.json"),"{\"dark\":true}");
        var window=new MainWindow(directory);var graph=window.Graph;
        Check(graph.Document.Serialize()==doc.Serialize()&&graph.Dark,"Legacy recent file, settings and separate text load without mutation");
        void Select(string id)
        {
            graph.ClearAllSelection();var n=graph.Document.Node(id);var area=n?.Kind=="circle"?graph.ObjectBounds(n):n!=null?new Rect(GraphStyle.Center(n)-new Vector(1,1),new Size(2,2)):GraphBoard.Bounds(graph.Document.Regions.Single(r=>r.Id==id));
            graph.BeginBoxSelection(area.TopLeft,false);graph.UpdateBoxSelection(area.BottomRight);graph.FinishBoxSelection();
        }
        void Action(string label)=>window.EditMenu.Items.OfType<MenuItem>().Single(m=>(string)m.Header==label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Select("a");Check(window.PropositionBox.Text=="Existing claim"&&window.NotesBox.Text=="Existing notes","Editors load separate existing fields");
        var root=(FrameworkElement)window.Content;root.Measure(new Size(1380,880));root.Arrange(new Rect(0,0,1380,880));root.UpdateLayout();
        Check(window.EditorLayout.RowDefinitions[2].ActualHeight>150&&Math.Abs(window.EditorLayout.RowDefinitions[1].ActualHeight-2*window.EditorLayout.RowDefinitions[2].ActualHeight)<=1.1,"Notes occupy the bottom third within pixel rounding");
        var node=graph.Document.Node("a")!;var radius=GraphStyle.Radius(node);var position=GraphStyle.Center(node);var claim=node.Title;
        window.NotesBox.Text=new string('n',18000)+"\n\u7b14\u8bb0";
        Check(node.Note==window.NotesBox.Text&&node.Title==claim&&GraphStyle.Radius(node)==radius&&GraphStyle.Center(node)==position,"Long Unicode notes never affect point size, position or proposition");
        var longNote=node.Note;Action("Undo");Select("a");Check(window.NotesBox.Text=="Existing notes"&&window.PropositionBox.Text==claim,"Undo restores notes independently");Action("Redo");Select("a");Check(window.NotesBox.Text==longNote,"Redo restores the note");
        window.PropositionBox.Text=new string('p',1600);Check(GraphStyle.Radius(graph.Document.Node("a")!)>radius&&graph.Document.Node("a")!.Note==longNote,"Only proposition edits grow the point");
        Select("b");Check(window.NotesBox.Text=="Other notes","Selecting another point does not leak notes");Select("a");Check(window.NotesBox.Text==longNote,"Returning to a point restores its own notes");
        foreach(var id in new[]{"group","frame"})
        {
            Select(id);Check(window.NotesBox.Text==(id=="group"?"Group notes":"Frame notes"),"Group and frame notes populate the lower editor");
            var before=id=="group"?GraphStyle.Radius(graph.Document.Node(id)!):0;
            window.NotesBox.Text=id+new string('x',20000);
            Check(id=="group"?GraphStyle.Radius(graph.Document.Node(id)!)==before:graph.Document.Regions.Single().Note==window.NotesBox.Text,"Group radius ignores notes and frame notes remain editable");
        }
        void SelectRelation(){graph.ClearAllSelection();graph.SelectedEdge=graph.Document.Edges.Single().Id;window.SelectionChanged();}
        SelectRelation();Check(window.EdgePanel.Visibility==Visibility.Visible&&window.NotesPanel.Visibility==Visibility.Visible&&window.NodePanel.Visibility==Visibility.Collapsed&&window.NotesBox.Text=="","Legacy relation opens with an empty notes editor below its arrows");
        var positions=graph.Document.Nodes.Select(n=>(n.Id,n.X,n.Y,Size:GraphStyle.Radius(n))).ToArray();
        var relationNote="Evidence for this connection.\n\u5173\u7cfb\u7b14\u8bb0\nSecond paragraph.";window.NotesBox.Text=relationNote;
        Check(graph.Document.Edges.Single().Note==relationNote&&positions.SequenceEqual(graph.Document.Nodes.Select(n=>(n.Id,n.X,n.Y,Size:GraphStyle.Radius(n)))),"Relation notes save independently without changing node geometry");
        window.DirectionChoices.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.ArrowChoices.Children.OfType<Button>().ElementAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(graph.Document.Edges.Single().Note==relationNote&&window.NotesBox.Text==relationNote,"Changing direction and symbol preserves relation notes");
        Action("Undo");Action("Undo");Action("Undo");SelectRelation();Check(window.NotesBox.Text=="","Undo can restore the old empty relation note");Action("Redo");SelectRelation();Check(window.NotesBox.Text==relationNote,"Redo restores relation notes");
        Select("b");Check(window.NotesBox.Text=="Other notes","Switching from a relation back to a point keeps their notes independent");SelectRelation();Check(window.NotesBox.Text==relationNote,"Reselecting a relation restores its own notes");
        var updated=graph.Document.Serialize();window.Close();Check(GraphDocument.Parse(File.ReadAllText(legacyFile)).Serialize()==updated,"Both fields and relation notes are saved to the original legacy file");
        window=new MainWindow(directory);Check(window.Graph.Document.Serialize()==updated,"Restart preserves both fields without unifying them");window.Close();
        foreach(var pair in new[]{("\u652f\u6301","Support"),("\u53cd\u9a73","Opposition"),("\u63a8\u5bfc","Inference"),("\u9650\u5b9a","Qualification"),("\u5b9a\u4e49","Definition"),("\u76f8\u5173","Association")})Check(GraphStyle.RelationName(pair.Item1)==pair.Item2&&GraphStyle.Marker(pair.Item1)==GraphStyle.Marker(pair.Item2),"Legacy built-in relations retain symbols and display in English");
        Check(GraphStyle.RelationName("Custom relation")=="Custom relation","Custom relation text remains untouched");
        Check(GraphDocument.Parse(updated).Markdown().Contains(longNote)&&GraphDocument.Parse(updated).Markdown().Contains("[Support]")&&GraphDocument.Parse(updated).Markdown().Contains("    Evidence for this connection."),"Markdown includes both object notes and multiline relation notes");
        var oldJson=System.Text.Json.Nodes.JsonNode.Parse(updated)!;oldJson["Edges"]![0]!.AsObject().Remove("Note");Check(GraphDocument.Parse(oldJson.ToJsonString()).Edges.Single().Note=="","Documents without a relation-note field remain compatible");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: two-thirds proposition / one-third notes, title-only radii, independent object and relation notes, undo/redo and Unicode persistence, relation notes survive symbol/direction changes and restart, legacy compatibility and Markdown export.\n");
    }
}
