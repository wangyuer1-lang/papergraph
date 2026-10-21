using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Text.Json;
namespace Papergraph;

public partial class MainWindow
{
    internal async Task RunFullTextReferenceTests()
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS: "+message);}
        async Task Flush(){UpdateLayout();await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);UpdateLayout();}
        Relation Ref(string id,string from,string to,string direction="forward")=>new(){Id=id,From=from,To=to,TextRole="reference",Direction=direction};
        var sample=new GraphDocument{Nodes=[
            new(){Id="claim",Title="细胞会分裂。"},new(){Id="next",Title="组织继续生长。",X=400},
            new(){Id="source",Title="张某，2020",Note="Private source notes",Caption="This caption is not a citation"},
            new(){Id="second-source",Title="李某，2021"},new(){Id="isolated",Title="Excluded"}
        ],Edges=[new(){Id="body",From="claim",To="next"},Ref("cite","source","claim"),Ref("reverse","claim","second-source","reverse"),Ref("duplicate","source","claim")]};
        string serialized=sample.Serialize();var built=GraphFullText.Build(sample);var first=built.Pages[0].Pieces[0];
        Check(built.Text=="细胞会分裂。（张某，2020；李某，2021）组织继续生长。","Citations follow the arrow target in one pair of parentheses");
        Check(first.References.Select(r=>r.NodeId).SequenceEqual(new[]{"source","second-source"}),"Forward and reverse arrows preserve source identity and suppress duplicate links");
        Check(TextStatistics.Count("甲。乙。（参考。）")==new TextStatistics(4,9)&&TextStatistics.Count("Changed. (b) d")==new TextStatistics(3,12),"Full text statistics include displayed citations and punctuation");
        Check(built.Pieces.Select(p=>p.NodeId).SequenceEqual(new[]{"claim","next"})&&built.ExcludedPoints==1,"Citations do not enter body ordering and displayed sources are not counted as excluded");
        Check(sample.Serialize()==serialized&&!built.Text.Contains("Private")&&!built.Text.Contains("caption"),"Projection preserves source files and excludes notes and captions");
        sample.Edges.Add(Ref("self","claim","claim"));sample.Edges.Add(Ref("both","source","next","both"));
        sample.Nodes.Add(new(){Id="empty",Title="  "});sample.Edges.Add(Ref("empty-cite","empty","claim"));
        built=GraphFullText.Build(sample);
        Check(built.Text==GraphFullText.Build(GraphDocument.Parse(serialized)).Text&&built.Issues.Any(i=>i.Code=="reference-direction")&&built.Issues.Any(i=>i.Code=="reference-empty")&&built.Issues.Any(i=>i.Code=="reference-endpoints"),"Ambiguous, empty and self references are explained without fabricating citation text");
        var english=new GraphDocument{Nodes=[new(){Id="a",Title="First claim."},new(){Id="b",Title="Next claim."},new(){Id="r",Title="Author, 2020"},new(){Id="s",Title="Nested source"}],Edges=[new(){Id="ab",From="a",To="b"},Ref("ra","r","a"),Ref("sr","s","r")]};
        Check(GraphFullText.Build(english).Text=="First claim. (Author, 2020) Next claim.","English references use spaced parentheses without recursively expanding references");
        english.Edges.Add(new(){Id="rs",From="r",To="s"});built=GraphFullText.Build(english);
        Check(built.Pages.Count==2&&built.Pages[0].Id=="ab"&&built.Pages[1].Id=="rs","A reference between manuscripts does not merge or reorder pages");
        Check(GraphFullText.Build(GraphDocument.Parse(english.Serialize())).Text==built.Text,"Reference rendering survives save and reload without storing generated brackets in nodes");
        english.Edges.Add(Ref("rb","r","b"));english.Node("r")!.BodyColors=[new(0,6,"#397FC4")];
        ResetDocument(english,FreshPath("Reference editing"),true);Graph.CancelLayout();SetFullTextVisible(true,false);SelectFullTextPage(0);await Flush();
        Check(manuscript.Text=="First claim. (Author, 2020) Next claim. (Author, 2020)","The editable full text matches the reference projection");
        string HighlightedText()=>string.Concat(fullTextBuffer!.ReferenceHighlights(dark).Select(r=>manuscript.Text!.Substring(r.Start,r.Length)));
        bool HasRenderedBackground()=>manuscript.GetVisualDescendants().OfType<BodyTextPresenter>().Single().TextLayout.TextLines.SelectMany(l=>l.TextRuns)
            .Any(r=>r.Properties?.BackgroundBrush is SolidColorBrush b&&b.Color==Color.Parse(FullTextEditBuffer.ReferenceHighlightColor(dark)));
        Check(HighlightedText()=="(Author, 2020)(Author, 2020)"&&HasRenderedBackground(),"All references have automatic background highlights including brackets, without highlighting body text or its spaces");
        Check(new[]{false,true}.All(d=>GraphTextColors.Palette.All(c=>c.Color!=FullTextEditBuffer.ReferenceHighlightColor(d))),"Reference backgrounds are distinct from all text color choices");
        void Input(int start,int end,string value){manuscript.Focus();manuscript.SelectionStart=start;manuscript.SelectionEnd=end;manuscript.SelectedText=value;}
        var references=fullTextBuffer!.Spans.Where(s=>s.NodeId=="r").ToArray();var second=references[1];
        FocusTextObject("r",false);
        Check(fullTextPage==0,"Clicking a reference stays on the cited manuscript when its source belongs to another page");
        manuscript.Focus();manuscript.SelectionStart=second.Start;manuscript.SelectionEnd=second.Start+6;
        manuscript.RaiseEvent(new TextInputEventArgs{RoutedEvent=TextInputEvent,Text="Researcher"});
        await Flush();references=fullTextBuffer!.Spans.Where(s=>s.NodeId=="r").ToArray();
        Check(doc.Node("r")!.Title=="Researcher, 2020"&&doc.Node("a")!.Title=="First claim."&&doc.Node("b")!.Title=="Next claim.","Editing inside parentheses updates the reference node only");
        Check(manuscript.Text=="First claim. (Researcher, 2020) Next claim. (Researcher, 2020)"&&bodyBox.Text=="Researcher, 2020","Repeated citations and the proposition panel update together");
        Check(manuscript.CaretIndex==references[1].Start+"Researcher".Length&&manuscript.IsKeyboardFocusWithin,"Updating an earlier repeated citation keeps the caret in the edited occurrence");
        Check(fullTextBuffer.Colors(doc).Count(c=>c.Color=="#397FC4")==2,"Every displayed citation retains its source text color");
        Check(HighlightedText()=="(Researcher, 2020)(Researcher, 2020)"&&HasRenderedBackground(),"Editing and selecting a citation retains backgrounds on every repeated occurrence");
        string unchanged=doc.Serialize();int open=references[1].Start-1;
        Input(open,open+1,"");await Flush();
        Check(doc.Serialize()==unchanged&&manuscript.Text!.Contains("(Researcher, 2020)"),"Generated citation parentheses cannot be deleted into a proposition");
        Input(0,references[0].End,"Overwrite across sources");await Flush();
        Check(doc.Serialize()==unchanged,"Replacing a proposition and its citation together is rejected atomically");
        UndoFullText(false);await Flush();
        Check(doc.Node("r")!.Title=="Author, 2020"&&manuscript.Text=="First claim. (Author, 2020) Next claim. (Author, 2020)","Undo restores all appearances of a cited source");
        Check(HighlightedText()=="(Author, 2020)(Author, 2020)","Undo restores citation highlight boundaries");
        UndoFullText(true);await Flush();
        Check(doc.Node("r")!.Title=="Researcher, 2020"&&fullTextPage==0,"Redo restores citation edits without changing pages");
        var body=fullTextBuffer!.Spans.First(s=>s.NodeId=="a");Input(body.Start,body.End,"正文命题");await Flush();
        Check(manuscript.Text!.StartsWith("正文命题（Researcher, 2020）")&&doc.Node("a")!.Title=="正文命题","Editing the body preserves citations and updates bracket typography");
        Check(HighlightedText()=="（Researcher, 2020）(Researcher, 2020)","Chinese bracket changes preserve reference backgrounds");
        RefreshFullText();Check(manuscript.Text==GraphFullText.Build(doc).Pages[0].Text,"Editable text and copy/export projection contain the same references");
        SelectFullTextPage(1);await Flush();
        Check(manuscript.Text!.Contains("Researcher, 2020"),"The cited source's own manuscript shows the saved update");
        Check(!fullTextBuffer!.ReferenceHighlights(dark).Any(r=>r.Start==0),"The source's own body is not highlighted as a citation on another page");
        Save();Check(GraphDocument.Parse(File.ReadAllText(file)).Node("r")!.Title=="Researcher, 2020","Only the actual source text is saved, without generated brackets");
        SelectFullTextPage(0);FocusTextObject("ra",false);await Flush();
        Check(!((Button)directionButtons.Children[2]).IsEnabled,"Reference inspector disables bidirectional arrows");
        unchanged=doc.Serialize();SetDirection("ra","both");
        Check(doc.Serialize()==unchanged,"Reference direction changes reject bidirectional arrows without changing data");
        ShowGraphContext();
        Check(graphContextMenu!.ItemsSource!.OfType<MenuItem>().Single(i=>i.Header?.ToString()==T("↔ Both directions")).IsEnabled==false,"Reference context menu also disables bidirectional arrows");
        graphContextMenu.Close();
        SetDirection("ra","reverse");await Flush();
        Check(doc.Edges.Single(e=>e.Id=="ra").Direction=="reverse"&&GraphFullText.Build(doc).Pages[1].Text.Contains("(正文命题; Nested source)"),"A reversed reference attaches text to the actual arrow target");
        Undo();await Flush();
        Check(doc.Edges.Single(e=>e.Id=="ra").Direction=="forward","Undo restores reference direction and citation attachment");
        SetTextRole("ra","flow");SetDirection("ra","both");
        unchanged=doc.Serialize();SetTextRole("ra","reference");
        Check(doc.Serialize()==unchanged&&!GraphFullText.IsReference(doc.Edges.Single(e=>e.Id=="ra")),"A bidirectional Body arrow must be given one direction before becoming a reference");
        SetDirection("ra","forward");SetTextRole("ra","reference");await Flush();
        var request=JsonSerializer.SerializeToElement(new{expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),requestId=Guid.NewGuid().ToString(),edges=new[]{new{id="ra",direction="both"}}});
        bool rejected=false;unchanged=doc.Serialize();
        try{AgentGraphEdit.Prepare(doc,file,request);}catch(InvalidDataException ex){rejected=ex.Message==GraphFullText.ReferenceDirectionHint;}
        Check(rejected&&doc.Serialize()==unchanged,"The agent edit API rejects bidirectional references atomically");
        SelectFullTextPage(0);FocusTextObject("r",false);toast.IsVisible=false;
        foreach(var language in new[]{"en","zh-CN","ja"}){
            SetLanguage(language);await Flush();
            const string help="Reference material → cited proposition. Adds the material in parentheses after that proposition.";
            Check(language=="en"?T(help)==help:T(help)!=help,"Reference direction help is translated in "+language);
            Check(language=="en"?T(GraphFullText.ReferenceDirectionHint)==GraphFullText.ReferenceDirectionHint:T(GraphFullText.ReferenceDirectionHint)!=GraphFullText.ReferenceDirectionHint,"Single-arrow guidance is translated in "+language);
        }
        using(var bitmap=new RenderTargetBitmap(new PixelSize((int)Width,(int)Height),new Vector(96,96))){bitmap.Render(this);bitmap.Save(Path.Combine(dataDir,"full-text-references.png"));}
        unchanged=doc.Serialize();
        manuscript.Focus();manuscript.SelectAll();await Flush();
        Check(!HasRenderedBackground()&&doc.Serialize()==unchanged,"Native text selection remains visible above citation backgrounds");
        foreach(bool isDark in new[]{false,true}){
            dark=isDark;ApplyTheme();RefreshFullText();manuscript.SelectionStart=manuscript.SelectionEnd=0;await Flush();
            Check(HasRenderedBackground()&&doc.Serialize()==unchanged,"Theme changes update citation backgrounds without changing manuscript data: "+(dark?"dark":"light"));
            using var bitmap=new RenderTargetBitmap(new PixelSize((int)Width,(int)Height),new Vector(96,96));bitmap.Render(this);bitmap.Save(Path.Combine(dataDir,"reference-highlight-"+(dark?"dark":"light")+".png"));
        }
        File.WriteAllText(Path.Combine(dataDir,"full-text-reference-results.txt"),$"{checks} checks passed.");
    }
}
