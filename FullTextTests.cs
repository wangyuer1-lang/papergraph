using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Papergraph;

public static class FullTextTests
{
    static void Check(bool ok,string message){if(!ok)throw new Exception("Full text: "+message);}
    static Proposition N(string id,string? text=null,string? parent=null,double x=0,double y=0)=>new(){Id=id,Title=text??id,Parent=parent,X=x-125,Y=y-60};
    static Relation E(string a,string b,string direction="forward",string? role=null,int order=0)=>new(){Id=a+"-"+b,From=a,To=b,Direction=direction,TextRole=role,TextOrder=order};
    static Region F(string id,double x,double y,double width=800,double height=600)=>new(){Id=id,Title=id,IsAbsolute=true,X=x,Y=y,Width=width,Height=height};
    static string Ids(FullTextResult result)=>string.Join(",",result.Pieces.Select(p=>p.NodeId));
    public static void Run()
    {
        TestStatistics();
        var doc=new GraphDocument{Nodes=[N("a","甲。"),N("b","乙。"),N("isolate","不进入正文"),N("ref","参考材料")],Edges=[E("a","b"),E("ref","b",role:"reference")]};
        doc.Nodes[0].Note="私人 Notes";
        var saved=doc.Serialize();var r=GraphFullText.Build(doc);
        Check(r.Text=="甲。乙。"&&Ids(r)=="a,b"&&r.ExcludedPoints==2&&r.ReferenceEdges==1,"Only directed body links include point text; notes and reference-only endpoints stay out");
        Check(doc.Serialize()==saved,"Projection never modifies its input");
        doc.Edges[0].Direction="reverse";Check(GraphFullText.Build(doc).Text=="乙。甲。","Reverse arrows reverse reading order");
        doc.Edges[0].Direction="both";r=GraphFullText.Build(doc);Check(r.Text==""&&r.Issues.Any(i=>i.Code=="bidirectional"),"Both-headed arrows are unresolved, never arbitrarily directed");
        doc=new(){Nodes=[N("a"),N("b"),N("c"),N("d")],Edges=[E("a","b",order:2),E("a","c",order:1),E("b","d"),E("c","d")]};
        r=GraphFullText.Build(doc);Check(Ids(r)=="a,c,b,d"&&!r.Issues.Any(i=>i.Code=="branch"),"Ordered branches finish before one shared join");
        doc.Edges[0].TextOrder=0;r=GraphFullText.Build(doc);Check(r.Pieces.Count==4&&r.Issues.Any(i=>i.Code=="branch"),"Missing branch order is explicitly reported");
        doc.Edges.Add(E("d","a"));r=GraphFullText.Build(doc);Check(r.Text.Length==0&&r.BlockedPoints==4&&r.Issues.Any(i=>i.Code=="cycle"),"Cycles do not produce fabricated reading order or repeated text");
        doc.Nodes.AddRange([N("x"),N("y")]);doc.Edges.Add(E("x","y"));Check(Ids(GraphFullText.Build(doc))=="x,y","An independent valid chain survives a cycle elsewhere");

        doc=new(){Nodes=[N("a","前。",x:100,y:100),new(){Id="ring",Kind="circle",Expanded=true,Title="圈的概括不重复进正文"},
            N("b","圈一。","ring",300,100),N("c","圈二。","ring",500,100),N("unused","组内孤立点","ring",400,200),
            N("d","后。",x:700,y:100),N("e","另一段。",x:100,y:900)],
            Regions=[F("first",0,0),F("second",0,800)],Edges=[E("a","ring"),E("b","c"),E("ring","d"),E("first","second"),E("d","e")]};
        r=GraphFullText.Build(doc);Check(r.Text=="前。\n圈一。圈二。\n后。\n\n另一段。","Circles split lines; frame changes split paragraphs; wrappers do not repeat their summaries");
        Check(!r.Text.Contains("孤立")&&r.Pieces.Select(p=>p.NodeId).Distinct().Count()==5,"Enclosing a point does not include it without a body arrow");
        doc.Nodes.Add(new(){Id="nested",Kind="circle",Expanded=true,Parent="ring"});
        doc.Node("b")!.Parent="nested";doc.Node("c")!.Parent="nested";
        r=GraphFullText.Build(doc);Check(r.Text=="前。\n圈一。圈二。\n后。\n\n另一段。","Nested circles do not duplicate or add empty lines");
        doc.Regions.Add(F("wrapper",-100,-100,1100,1700));
        r=GraphFullText.Build(doc);Check(r.Pieces.Count==5&&r.Text.Contains("\n\n另一段。"),"An outer wrapper preserves inner paragraph boundaries");
        doc.Regions.Add(F("overlap",50,50,760,550));r=GraphFullText.Build(doc);Check(r.Pieces.Count==5&&r.Issues.Any(i=>i.Code=="overlap"),"Overlap is reported and never duplicates a point");

        doc=new(){Nodes=[N("a"),N("b"),N("c")],Edges=[E("a","b"),E("b","c"),E("a","c")]};
        r=GraphFullText.Build(doc);Check(Ids(r)=="a,b,c"&&!r.Issues.Any(i=>i.Code=="branch"),"Transitive links are not falsely classified as an unordered branch");
        Check(!doc.Serialize().Contains("TextRole")&&!doc.Serialize().Contains("TextOrder"),"Old default relations retain their serialized representation");
        doc.Edges[0].TextRole="reference";doc.Edges[0].TextOrder=2;
        var clone=GraphDocument.Parse(doc.Serialize());Check(clone.Edges[0].TextRole=="reference"&&clone.Edges[0].TextOrder==2,"Role and branch order round-trip");
        var fragment=GraphClipboard.Capture(doc,["a","b","c"],[]);var pasted=GraphClipboard.PreparePaste(new(),fragment,new Point(10,10));
        Check(pasted.Document.Edges.Any(e=>e.TextRole=="reference"&&e.TextOrder==2),"Graph copy/paste preserves text roles and order");
        doc.Edges[0].TextRole="invalid";bool rejected=false;try{doc.Validate();}catch{rejected=true;}Check(rejected,"Invalid roles are rejected");
        TestPages();
        TestLiveWindow();
        TestPageWindow();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: live full text, forward/reverse, citation exclusion, isolated points, circles, nested/overlapping frames, branch ordering and merge deduplication, cycle/bidirectional diagnostics, source navigation, undo/redo, persistence and clipboard.\n");
    }
    static void TestStatistics()
    {
        Check(TextStatistics.Count("")==new TextStatistics(0,0),"Empty text has zero counts");
        Check(TextStatistics.Count("\r\n\t　 ")==new TextStatistics(0,0),"Formatting whitespace is not counted");
        Check(TextStatistics.Count("植物胚胎，3D model。\nCellNexus 2.0")==new TextStatistics(8,25),"Mixed Chinese and English separate words from characters and punctuation");
        Check(TextStatistics.Count("α-synuclein O’Neill can't").Words==3,"Scientific hyphenation and apostrophes stay within words");
        Check(TextStatistics.Count("𠀀，e\u0301 👩‍🔬！")==new TextStatistics(2,5),"Supplementary Han, combining accents and emoji are counted as visible characters");
        Check(TextStatistics.Count("胚の発生、カナ。")==new TextStatistics(6,8),"Japanese text counts kana and Han but not punctuation as words");
    }
    static void TestPages()
    {
        var doc=new GraphDocument{Nodes=[N("a",x:50,y:50),N("b",x:150,y:50),N("c",x:50,y:250),N("d",x:150,y:250),N("isolate",x:250,y:250)],
            Edges=[E("c","d"),E("a","b"),E("b","c",role:"reference")],Regions=[F("shared",0,0)]};
        var saved=doc.Serialize();var r=GraphFullText.Build(doc);
        Check(r.Pages.Count==2&&r.Pages[0].Text=="c d"&&r.Pages[1].Text=="a b","Disconnected chains get separate pages ordered by their first Body edge, not position or node creation");
        Check(r.Text=="c d\fa b"&&r.ExcludedPoints==1&&!r.Issues.Any(i=>i.Code=="multiple-starts"),"A shared frame and reference link do not concatenate pages or include isolated points");
        Check(doc.Serialize()==saved,"Pagination does not create or modify graph objects");
        doc.Regions.Add(F("empty-next",0,800));doc.Edges.Add(E("shared","empty-next"));
        r=GraphFullText.Build(doc);
        Check(r.Pages.Count==2&&r.Pages[0].Text=="c d"&&r.Pages[1].Text=="a b","External frame arrows and empty framework sections do not merge two disconnected manuscripts inside one frame or create blank pages");
        doc.Node("a")!.Y=350;doc.Node("c")!.Y=0;
        Check(GraphFullText.Build(doc).Pages.Select(p=>p.Id).SequenceEqual(r.Pages.Select(p=>p.Id)),"Moving structures does not reorder pages");
        doc.Edges.Add(new(){Id="both",From="a",To="c",Direction="both"});
        r=GraphFullText.Build(doc);Check(r.Pages.Count==2&&r.Issues.Any(i=>i.Code=="bidirectional"),"Bidirectional arrows are diagnosed without joining pages");
        doc.Edges.Single(e=>e.Id=="b-c").TextRole=null;
        r=GraphFullText.Build(doc);Check(r.Pages.Count==1&&r.Text=="a b c d"&&r.Pages[0].Id=="c-d","A Body bridge merges pages in arrow order while retaining the earliest component position");
        doc.Edges.Single(e=>e.Id=="b-c").Direction="reverse";
        r=GraphFullText.Build(doc);Check(r.Pages.Count==1,"Reversing a bridge changes ordering, not component membership");
        doc.Edges.RemoveAll(e=>e.Id=="b-c");
        r=GraphFullText.Build(GraphDocument.Parse(doc.Serialize()));
        Check(r.Pages.Count==2&&r.Pages[0].Text=="c d"&&r.Pages[1].Text=="a b","Removing a bridge splits pages and save/load preserves creation order");
        doc.Nodes.Add(new(){Id="ring",Kind="circle",Expanded=true});
        foreach(var n in doc.Nodes.Where(n=>n.Kind=="point"))n.Parent="ring";
        doc.Regions.Clear();doc.Edges.RemoveAll(e=>e.From=="shared");
        Check(GraphFullText.Build(doc).Pages.Count==2,"A shared circle without Body endpoint connections does not merge separate chains");
        doc.Nodes.Add(N("outside",x:1500,y:1500));doc.Edges.Add(E("ring","outside"));
        r=GraphFullText.Build(doc);Check(r.Pages.Count==3&&r.Pieces.Count==5&&r.ExcludedPoints==1&&r.GeneralIssues.Any(i=>i.Code=="container-pages"),"A circle outline cannot silently concatenate independent member manuscripts");
        doc.Edges.Add(E("b","c"));r=GraphFullText.Build(doc);
        Check(r.Pages.Count==1&&r.Pieces.Count==5&&r.ExcludedPoints==1,"Connecting the members allows a circle Body arrow to continue the whole manuscript, excluding isolated members");

        doc=new(){Nodes=[N("a",x:50,y:50),N("b",x:100,y:50),N("c",x:50,y:950),N("d",x:100,y:950)],
            Regions=[F("one",0,0),F("two",0,800)],Edges=[E("c","d"),E("a","b")]};
        Check(GraphFullText.Build(doc).Pages.Count==2,"Separate frames have separate text pages before they are connected");
        doc.Edges.Add(E("one","two"));r=GraphFullText.Build(doc);
        Check(r.Pages.Count==1&&r.Text=="a b\n\nc d","Frame-to-frame Body arrows merge pages while retaining paragraph breaks and reading direction");
        doc.Edges.Add(E("d","c"));r=GraphFullText.Build(doc);
        Check(r.Pages.Count==1&&r.Pages[0].BlockedPoints==2&&r.Pages[0].Issues.Any(i=>i.Code=="cycle"),"Cycles remain diagnosed within their own page");
        doc.Edges.RemoveAll(e=>e.From=="one");r=GraphFullText.Build(doc);
        Check(r.Pages.Count==2&&r.Pages[0].Pieces.Count==0&&r.Pages[0].BlockedPoints==2&&r.Pages[1].Text=="a b","An entirely blocked page is retained without hiding another independent page");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: independent text pages, creation order, reference and bidirectional isolation, connect/split, nested containers, frame connections, cycle isolation and persistence.\n");
    }
    static void Pump()
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(260)};
        timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    static void TestLiveWindow()
    {
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-full-text-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"sample.papergraph");
        var doc=new GraphDocument{Nodes=[N("a","甲。"),N("b","乙。"),N("reference","参考。")],Edges=[E("a","b"),E("reference","b",role:"reference")]};
        Storage.Save(path,doc);File.WriteAllText(Path.Combine(directory,"recent.txt"),"sample.papergraph");
        var window=new MainWindow(directory);window.SetFullTextVisible(true,false);
        Check(window.CurrentFullText.Text=="甲。乙。","Window preview starts from the entire graph");
        Check(window.FullTextCount.Text=="Words: 2 · Characters: 4","Header counts only the current manuscript, excluding references");
        var firstRun=((System.Windows.Documents.Paragraph)window.FullTextBox.Document.Blocks.FirstBlock).Inlines.OfType<System.Windows.Documents.Run>().First();
        window.FullTextBox.Selection.Select(firstRun.ContentStart,firstRun.ContentEnd);
        Check(window.FullTextCount.Text=="Words: 1 / 2 · Characters: 2 / 4","Selection shows selected words and characters against page totals");
        window.FullTextBox.Selection.Select(firstRun.ContentStart,firstRun.ContentStart);
        Check(window.FullTextCount.Text=="Words: 2 · Characters: 4","Clearing a selection restores page totals");
        window.FocusTextObject("a");Check(window.Graph.Selected.SetEquals(["a"])&&window.PropositionBox.Text=="甲。","Clicking a text source locates the graph object and editor");
        window.PropositionBox.Text="修改。";Pump();Check(window.CurrentFullText.Text=="修改。乙。","Typing updates preview before manual saving");
        Check(window.FullTextCount.Text=="Words: 3 · Characters: 5","Typing refreshes counts without saving");
        window.NotesBox.Text="人工笔记不在正文";Pump();Check(!window.CurrentFullText.Text.Contains("笔记"),"Notes edits remain out of manuscript text");
        Check(window.FullTextCount.Text=="Words: 3 · Characters: 5","Notes do not affect counts");
        window.SetTextRole("a-b","reference");Pump();Check(window.CurrentFullText.Text=="","Role changes immediately remove citation-only content");
        Check(window.FullTextCount.Text=="Words: 0 · Characters: 0","No manuscript resets both counters");
        var undo=window.EditMenu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()=="Undo");undo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
        Check(window.CurrentFullText.Text=="修改。乙。","Undo updates the preview and restores role");
        Check(window.FullTextCount.Text=="Words: 3 · Characters: 5","Undo restores page counts");
        var redo=window.EditMenu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()=="Redo");redo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
        Check(window.CurrentFullText.Text.Length==0,"Redo updates preview");
        window.FocusTextObject("a-b");window.SetTextRole("a-b","flow");window.TextOrderBox.Text="3";Pump();
        Check(window.Graph.Document.Edges.Single(e=>e.Id=="a-b").TextOrder==3,"Inspector branch order is saved");
        var response=JsonSerializer.SerializeToElement(window.HandleAgent(JsonSerializer.SerializeToElement(new{operation="fullText"})));
        Check(response.GetProperty("preview").GetProperty("Text").GetString()==window.CurrentFullText.Text,"Read-only API and on-page preview agree");
        var before=window.Graph.Document.Serialize();
        window.SetFullTextVisible(false,false);window.SetFullTextVisible(true,false);Check(window.Graph.Document.Serialize()==before,"Toggling preview never changes graph data");
        window.Close();Check(GraphDocument.Parse(File.ReadAllText(path)).Serialize()==before,"Closing preserves text, notes and relation settings");
    }
    static void TestPageWindow()
    {
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-text-pages-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"sample.papergraph");
        var doc=new GraphDocument{Nodes=[N("a"),N("b"),N("c"),N("d")],Edges=[E("a","b"),E("c","d"),E("b","c",role:"reference")]};
        Storage.Save(path,doc);File.WriteAllText(Path.Combine(directory,"recent.txt"),"sample.papergraph");
        var window=new MainWindow(directory);window.SetFullTextVisible(true,false);
        string VisibleText()=>new System.Windows.Documents.TextRange(window.FullTextBox.Document.ContentStart,window.FullTextBox.Document.ContentEnd).Text.Trim();
        Check(window.CurrentFullText.Pages.Count==2&&VisibleText()=="a b"&&window.FullTextPageTabs.Children.Count==2,"Only the first connected manuscript is shown, with two numeric tabs");
        ((RadioButton)window.FullTextPageTabs.Children[1]).IsChecked=true;
        Check(window.CurrentFullTextPageIndex==1&&VisibleText()=="c d","Selecting the second tab replaces the displayed manuscript");
        window.FocusTextObject("a");Check(window.CurrentFullTextPageIndex==0&&VisibleText()=="a b","Selecting a point in another manuscript follows its text page");
        window.FocusTextObject("c");window.PropositionBox.Text="Changed.";Pump();
        Check(window.CurrentFullTextPageIndex==1&&VisibleText()=="Changed. d","Live edits preserve the selected page and update only its manuscript");
        Check(window.FullTextCount.Text=="Words: 2 · Characters: 9","Counts use the edited current page, not all pages");
        window.SelectFullTextPage(0);Check(window.FullTextCount.Text=="Words: 2 · Characters: 2","Switching pages replaces the count");
        window.SelectFullTextPage(1);
        window.SetTextRole("b-c","flow");Pump();
        Check(window.CurrentFullText.Pages.Count==1&&window.FullTextPagesScroller.Visibility==Visibility.Collapsed&&VisibleText()=="a b Changed. d","Connecting manuscripts merges visible text live and hides unnecessary tabs");
        Check(window.FullTextCount.Text=="Words: 4 · Characters: 11","A Body bridge updates totals for the merged manuscript");
        var undo=window.EditMenu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()=="Undo");undo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
        Check(window.CurrentFullText.Pages.Count==2&&window.CurrentFullTextPageIndex==1&&VisibleText()=="Changed. d","Undo splits pages and preserves the reader's component");
        Check(window.FullTextCount.Text=="Words: 2 · Characters: 9","Undoing the bridge restores current-page totals");
        var redo=window.EditMenu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()=="Redo");redo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
        Check(window.CurrentFullText.Pages.Count==1&&VisibleText()=="a b Changed. d","Redo merges pages again without duplicate text");
        window.SetTextRole("b-c","reference");Pump();
        var response=JsonSerializer.SerializeToElement(window.HandleAgent(JsonSerializer.SerializeToElement(new{operation="fullText"})));
        Check(response.GetProperty("preview").GetProperty("Pages").GetArrayLength()==2,"Read-only API exposes separate manuscripts");
        var saved=window.Graph.Document.Serialize();window.SelectFullTextPage(0);window.SelectFullTextPage(1);
        Check(window.Graph.Document.Serialize()==saved,"Switching pages leaves graph content unchanged");window.Close();
        var reopened=new MainWindow(directory);reopened.SetFullTextVisible(true,false);
        Check(reopened.CurrentFullText.Pages.Count==2&&reopened.CurrentFullText.Pages[0].Text=="a b"&&reopened.CurrentFullText.Pages[1].Text=="Changed. d","Restart retains page ordering");reopened.Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: live page tabs, source navigation across pages, live typing, automatic page merge/split, undo/redo, API pages and restart ordering.\n");
    }
    public static void VerifyFixture(string path,string output)
    {
        var doc=GraphDocument.Parse(File.ReadAllText(path));var before=doc.Serialize();var whole=GraphFullText.Build(doc);
        var frame=doc.Regions.Single(r=>r.Title=="abstract");
        var sample=GraphClipboard.Capture(doc,[],[frame.Id]).Graph;
        var actual=GraphFullText.Build(sample);
        var note=GraphNotes.AllText(frame);var marker="按顺序拼接的摘要草稿：";
        var start=note.IndexOf(marker,StringComparison.Ordinal);Check(start>=0,"Reference abstract exists");
        var expected=note[(start+marker.Length)..].Trim();
        string Normalize(string value)=>Regex.Replace(value,@"\s+","");
        Check(Normalize(actual.Text)==Normalize(expected),"Actual Chinese Layout abstract preserves every proposition in the original order");
        Check(actual.Pieces.Count==9&&actual.Issues.Count==0,"Actual abstract has nine unique body points, with its three rings expanded");
        Check(!whole.Pieces.Any(p=>doc.Node(p.NodeId)?.Caption.StartsWith("R1｜")==true),"Unconnected review propositions stay excluded");
        Check(doc.Serialize()==before,"Actual reference graph remains unchanged");
        File.WriteAllText(output,JsonSerializer.Serialize(new{whole,abstractText=actual.Text,abstractOrder=actual.Pieces.Select(p=>p.NodeId),referenceMatches=true},GraphDocument.Options));
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: paper · Layout 中文 fixture, all nine abstract propositions match the corresponding source text in order; ring summaries and unconnected review points excluded; source unchanged.\n");
    }
}
