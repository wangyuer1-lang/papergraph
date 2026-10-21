using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace Papergraph;

public partial class MainWindow
{
    internal async Task RunFullTextEditTests()
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS: "+message);}
        async Task Flush(){UpdateLayout();await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);UpdateLayout();}
        void Input(int start,int end,string text)
        {
            manuscript.Focus();manuscript.SelectionStart=start;manuscript.SelectionEnd=end;
            if(text.Length==0)manuscript.RaiseEvent(new KeyEventArgs{RoutedEvent=KeyDownEvent,Key=Key.Back});
            else manuscript.RaiseEvent(new TextInputEventArgs{RoutedEvent=TextInputEvent,Text=text});
        }
        FullTextEditBuffer.Span Range(string id)=>fullTextBuffer!.Spans.Single(s=>s.NodeId==id);
        var fixture=new GraphDocument{Title="Full text editing",AgentReadOnly=true,Nodes=[
            new(){Id="one",Caption="Background",Title="  第一段 🌱。  ",Note="Personal notes stay unchanged"},
            new(){Id="two",Caption="Research question",Title="第二段。",X=400},
            new(){Id="three",Title="Another page.",Y=600},
            new(){Id="four",Title="Separate source.",X=400,Y=600}
        ],Edges=[new(){Id="flow",From="one",To="two"},new(){Id="other",From="three",To="four"}]};
        fixture.Node("one")!.BodyColors=[new(6,2,"#D44747")];
        ResetDocument(fixture,FreshPath("Full text editing"),true);Graph.CancelLayout();
        SetFullTextVisible(true,false);FocusTextObject("one",false);await Flush();
        var presenter=manuscript.GetVisualDescendants().OfType<BodyTextPresenter>().Single();
        Check(!manuscript.IsReadOnly&&currentFullText.Pages.Count==2,"Connected text is editable and separate manuscripts keep their tabs");
        var point=manuscript.TranslatePoint(new Point(40,20),this)!.Value;var hit=this.InputHitTest(point) as Visual;
        Check(hit==manuscript||hit?.GetVisualAncestors().Contains(manuscript)==true,"The full text editor receives pointer input");
        string original=doc.Node("one")!.Title;
        Input(0,0,"新增 ");
        Check(doc.Node("one")!.Title=="  新增 第一段 🌱。  "&&doc.Node("two")!.Title=="第二段。","Typing writes only the source proposition and preserves hidden whitespace");
        Check(bodyBox.Text==doc.Node("one")!.Title&&ReferenceEquals(FocusManager?.GetFocusedElement(),manuscript),"The proposition editor updates without stealing focus");
        FocusTextObject("two",false);editKey="";Input(0,0,"键盘 ");
        Check(editorId=="one"&&bodyBox.Text==doc.Node("one")!.Title&&manuscript.IsKeyboardFocusWithin,"Typing after keyboard navigation selects the correct source without moving the caret");
        UndoFullText(false);
        Check(doc.AgentReadOnly&&doc.Node("one")!.Note=="Personal notes stay unchanged","Human editing preserves AI protection and personal notes");
        int caret=manuscript.CaretIndex;RefreshFullText();await Flush();
        Check(manuscript.CaretIndex==caret&&manuscript.Text!.StartsWith("新增 "),"Background refresh preserves the caret and typed spaces");
        Check(presenter.TextLayout.TextLines.SelectMany(l=>l.TextRuns).Any(r=>r.Properties?.ForegroundBrush is SolidColorBrush b&&b.Color==Color.Parse("#D44747")),"Full text preserves source colors while editing");
        UndoFullText(false);await Flush();
        Check(doc.Node("one")!.Title==original&&manuscript.Text!.StartsWith("第一段"),"Undo restores the proposition and manuscript together");
        UndoFullText(true);await Flush();
        Check(doc.Node("one")!.Title=="  新增 第一段 🌱。  ","Redo restores the same text in the source");
        int emoji=manuscript.Text!.IndexOf("🌱",StringComparison.Ordinal);
        Input(emoji,emoji+2,"🧬");
        Check(doc.Node("one")!.Title.Contains("🧬")&&doc.Node("one")!.BodyColors!.Any(c=>c.Length==2),"Replacing an emoji preserves complete Unicode and color ranges");
        var beforeUndo=doc.Node("one")!.Title;
        manuscript.RaiseEvent(new KeyEventArgs{RoutedEvent=KeyDownEvent,Key=Key.Z,KeyModifiers=KeyModifiers.Meta});
        Check(doc.Node("one")!.Title!=beforeUndo&&manuscript.IsKeyboardFocusWithin,"Command-Z uses graph undo and keeps the full text caret");
        manuscript.RaiseEvent(new KeyEventArgs{RoutedEvent=KeyDownEvent,Key=Key.Z,KeyModifiers=KeyModifiers.Meta|KeyModifiers.Shift});
        Check(doc.Node("one")!.Title==beforeUndo,"Command-Shift-Z redoes both views");
        RefreshFullText();var first=Range("one");Input(first.End,first.End,"\n");
        Input(manuscript.CaretIndex,manuscript.CaretIndex,"下一行 日本語");
        RefreshFullText();await Flush();
        Check(doc.Node("one")!.Title.Contains("\n下一行 日本語")&&manuscript.Text!.Contains("\n下一行 日本語"),"Newlines and Japanese input survive refresh without trimming the draft");
        presenter.PreeditText="中文输入";await Flush();
        Check(presenter.TextLayout.TextLines.Count>0,"Native IME preedit is retained in the full text editor");presenter.PreeditText=null;
        string beforeCross=doc.Serialize(),displayBefore=manuscript.Text!;
        var second=Range("two");Input(Range("one").Start,second.End,"Do not distribute this");
        Check(doc.Serialize()==beforeCross&&manuscript.Text==displayBefore,"Cross-proposition replacement is rejected atomically");
        Check(status.Text==T("Edit one proposition at a time. Text across propositions and automatic separators cannot be replaced together."),"Cross-proposition edits explain how to continue: "+status.Text);
        // Repeated text at a zero-width separator must use the selected source.
        var adjacent=new GraphDocument{Nodes=[new(){Id="a",Title="甲"},new(){Id="b",Title="甲"}],Edges=[new(){Id="ab",From="a",To="b"}]};
        var buffer=new FullTextEditBuffer(adjacent,GraphFullText.Build(adjacent).Pages[0]);
        Check(buffer.TryPrepare("甲甲甲",1,1,"a",out var boundary)&&boundary!.NodeId=="a"&&boundary.Body=="甲甲","Repeated characters at a boundary use the active source");
        Check(buffer.TryPrepare("甲甲甲",1,1,"b",out boundary)&&boundary!.NodeId=="b","The next proposition can own input at its start");
        var english=new GraphDocument{Nodes=[new(){Id="a",Title="Alpha"},new(){Id="b",Title="Beta"}],Edges=[new(){Id="ab",From="a",To="b"}]};
        buffer=new(english,GraphFullText.Build(english).Pages[0]);
        Check(!buffer.TryPrepare("AlphaBeta",5,6,null,out _),"Generated separators cannot silently merge propositions");
        // Delete and re-enter a whole body without letting projection trimming lose its owner.
        RefreshFullText();first=Range("one");Input(first.Start,first.End,"");
        RefreshFullText();await Flush();
        Check(Range("one").Start==Range("one").End,"An emptied proposition remains editable during the session");
        Input(Range("one").Start,Range("one").End,"恢复正文。");
        Check(doc.Node("one")!.Title=="  恢复正文。  ","An empty source can be filled again without creating or deleting nodes");
        first=Range("one");manuscript.SelectionStart=first.Start;manuscript.SelectionEnd=first.End;manuscript.SelectedText="替换正文。\n粘贴第二行。";
        Check(doc.Node("one")!.Title=="  替换正文。\n粘贴第二行。  ","A multiline replacement stays in its source proposition");
        var preserved=doc.Node("one")!.Title;SelectFullTextPage(1);await Flush();FocusTextObject("three",false);
        first=Range("three");Input(first.Start,first.Start,"Page two: ");
        Check(doc.Node("three")!.Title=="Page two: Another page."&&doc.Node("one")!.Title==preserved,"Editing another tab cannot write to the previous page");
        // Edits in the side panel and stale projections never overwrite each other.
        bodyBox.Focus();bodyBox.Text="Side editor changes.";RefreshFullText();
        Check(manuscript.Text!.StartsWith("Side editor changes."),"Proposition edits flow back into full text");
        string staleDisplay=manuscript.Text!;doc.Node("three")!.Title="External update.";Changed(false);
        manuscript.Text=staleDisplay+"stale";
        Check(doc.Node("three")!.Title=="External update."&&manuscript.Text!.StartsWith("External update."),"A stale edit refreshes instead of overwriting new source text");
        RefreshFullText();FocusTextObject("three",false);Input(0,0,"Saved ");
        await Task.Delay(800);await Flush();
        Check(GraphDocument.Parse(File.ReadAllText(file)).Node("three")!.Title=="Saved External update.","Full text changes are automatically saved");
        string savedPath=file;var expected=doc.Serialize();
        ResetDocument(new GraphDocument{Title="Other library graph"},FreshPath("Other library graph"),true);
        Check(manuscript.Text==""&&manuscript.IsReadOnly,"Switching graphs clears the previous text and source mapping");
        OpenPath(savedPath);SetFullTextVisible(true,false);await Flush();
        Check(doc.Serialize()==expected,"Reopening preserves text, colors, notes, links and permissions");
        foreach(var language in new[]{"en","zh-CN","ja"}){
            SetLanguage(language);SelectFullTextPage(0);FocusTextObject("one",false);manuscript.Focus();toast.IsVisible=false;await Flush();
            const string hint="Click to edit. Changes are saved to the source proposition.";
            Check(language=="en"?T(hint)==hint:T(hint)!=hint,"Editing help is translated in "+language);
            using var bitmap=new RenderTargetBitmap(new PixelSize((int)Width,(int)Height),new Vector(96,96));bitmap.Render(this);bitmap.Save(Path.Combine(dataDir,"full-text-edit-"+language+".png"));
        }
        File.WriteAllText(Path.Combine(dataDir,"full-text-edit-results.txt"),$"{checks} checks passed.");
        await RunFullTextReferenceTests();
    }
}
