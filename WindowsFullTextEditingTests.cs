using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Papergraph;

public partial class MainWindow
{
    internal void RunWindowsFullTextEditingTests()
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception("Windows full text editing: "+message);checks++;}
        void Flush(int milliseconds=30)
        {
            var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};
            timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
        }
        void Input(int start,int length,string value){FullTextBox.Focus();FullTextBox.Select(start,length);FullTextBox.Selection.Text=value;Flush();}
        FullTextEditBuffer.Span Range(string id)=>fullTextBuffer!.Spans.First(s=>s.NodeId==id);
        var fixture=new GraphDocument{Title="Windows full text editing",AgentReadOnly=true,Nodes=[
            new(){Id="one",Title="  第一段 🌱。  ",Note="Personal notes",BodyColors=[new(6,2,"#D44747")]},
            new(){Id="two",Title="第二段。",X=400},new(){Id="three",Title="Another page.",Y=600},
            new(){Id="four",Title="Separate source.",X=400,Y=600}],
            Edges=[new(){Id="flow",From="one",To="two"},new(){Id="other",From="three",To="four"}]};
        ResetDocument(fixture,FreshPath("Full text editing"),true);Graph.CancelLayout();
        Width=1380;Height=880;Show();Activate();SetFullTextVisible(true,false);FocusTextObject("one",false);Flush();
        Check(!FullTextBox.IsReadOnly&&CurrentFullText.Pages.Count==2,"Connected text is editable with separate page tabs");
        string original=doc.Node("one")!.Title;
        Input(0,0,"新增 ");
        Check(doc.Node("one")!.Title=="  新增 第一段 🌱。  "&&doc.Node("two")!.Title=="第二段。","Typing updates only its source and preserves hidden whitespace");
        Check(PropositionBox.Text==doc.Node("one")!.Title&&FullTextBox.IsKeyboardFocusWithin,"Source inspector updates without taking keyboard focus");
        int caret=FullTextBox.CaretIndex;RefreshFullText();Flush();
        Check(FullTextBox.CaretIndex==caret&&FullTextBox.Text.StartsWith("新增 "),"Refresh preserves caret and draft spaces");
        Check(doc.AgentReadOnly&&doc.Node("one")!.Note=="Personal notes","Manual editing preserves agent protection and notes");
        ApplicationCommands.Undo.Execute(null,FullTextBox);Flush();
        Check(doc.Node("one")!.Title==original&&FullTextBox.Text.StartsWith("第一段")&&FullTextBox.IsKeyboardFocusWithin,"Native Undo restores source and full text without losing focus");
        ApplicationCommands.Redo.Execute(null,FullTextBox);Flush();
        Check(doc.Node("one")!.Title=="  新增 第一段 🌱。  ","Native Redo restores the source edit");
        int emoji=FullTextBox.Text.IndexOf("🌱",StringComparison.Ordinal);
        editKey="";Input(emoji,2,"🧬");
        Check(doc.Node("one")!.Title.Contains("🧬")&&doc.Node("one")!.BodyColors!.Any(c=>c.Length==2),"Unicode replacement preserves complete emoji and color ranges");
        Check(FullTextBox.Document.Blocks.OfType<Paragraph>().SelectMany(p=>p.Inlines.OfType<Run>()).Any(r=>r.Text=="🧬"&&r.Foreground.ToString()=="#FFD44747"),"Source colors render after direct editing");
        FullTextBox.Select(Range("one").Start+1,0);FullTextBox.CaretPosition.InsertParagraphBreak();Flush();
        Check(doc.Node("one")!.Title.Contains('\n')&&FullTextBox.Text.Contains('\n'),"Native Enter paragraph structure saves one newline in its source");
        var first=Range("one");Input(first.Start,first.End-first.Start,"替换正文。\n日本語と中文。");
        Check(doc.Node("one")!.Title=="  替换正文。\n日本語と中文。  ","Multiline Chinese and Japanese replacement stays in one proposition");
        string before=doc.Serialize(),display=FullTextBox.Text;
        Input(Range("one").Start,Range("two").End,"Do not merge sources");
        Check(doc.Serialize()==before&&FullTextBox.Text==display,"Cross-source replacement is rejected atomically after the native edit completes");
        Check(ToastText.Text==Localization.Text("Edit one proposition at a time. Text across propositions and automatic separators cannot be replaced together."),"Rejected edits explain how to continue");
        first=Range("one");Input(first.Start,first.End-first.Start,"");RefreshFullText();Flush();
        Check(Range("one").Start==Range("one").End,"Empty source retains an editable insertion point");
        Input(Range("one").Start,0,"恢复正文。");
        Check(doc.Node("one")!.Title=="  恢复正文。  ","An emptied body can be re-entered without creating a node");
        SelectFullTextPage(1);FocusTextObject("three",false);Input(0,0,"Page two: ");
        Check(doc.Node("three")!.Title=="Page two: Another page."&&doc.Node("one")!.Title=="  恢复正文。  ","Another page writes only its own source");
        PropositionBox.Focus();PropositionBox.Text="Side editor changes.";RefreshFullText();
        Check(FullTextBox.Text.StartsWith("Side editor changes."),"Side editor changes flow into the manuscript");
        var stale=FullTextBox.Text;doc.Node("three")!.Title="External update.";Changed(false);FullTextBox.Text=stale+"stale";Flush();
        Check(doc.Node("three")!.Title=="External update."&&FullTextBox.Text.StartsWith("External update."),"Stale edits cannot overwrite a newer source revision");
        Input(0,0,"Saved ");Flush(1000);
        Check(GraphDocument.Parse(File.ReadAllText(file)).Node("three")!.Title=="Saved External update.","Direct edits are automatically saved");
        string savedPath=file,expected=doc.Serialize();
        ResetDocument(new GraphDocument{Title="Other graph"},FreshPath("Other graph"),true);RefreshFullText();
        Check(FullTextBox.Text==""&&FullTextBox.IsReadOnly,"Switching graphs clears old source mappings");
        ResetDocument(GraphDocument.Parse(File.ReadAllText(savedPath)),savedPath,false);RefreshFullText();
        Check(doc.Serialize()==expected,"Save/reopen preserves text, colors, notes, links and permissions");

        var cited=new GraphDocument{Nodes=[new(){Id="a",Title="Alpha"},new(){Id="b",Title="Beta",X=400},new(){Id="r",Title="Source",Y=400,BodyColors=[new(0,6,"#397FC4")]}],
            Edges=[new(){Id="ab",From="a",To="b"},new(){Id="ra",From="r",To="a",TextRole="reference"},new(){Id="rb",From="r",To="b",TextRole="reference"}]};
        ResetDocument(cited,FreshPath("Repeated citations"),true);RefreshFullText();FocusTextObject("b",false);
        Check(FullTextBox.Text=="Alpha (Source) Beta (Source)","Repeated citations start with generated parentheses");
        string HighlightedText()
        {
            var text=new System.Text.StringBuilder();var pointer=FullTextBox.Document.ContentStart;
            while(pointer.CompareTo(FullTextBox.Document.ContentEnd)<0){
                if(pointer.GetPointerContext(LogicalDirection.Forward)==TextPointerContext.Text){
                    var run=pointer.GetTextInRun(LogicalDirection.Forward);
                    if(new TextRange(pointer,pointer.GetPositionAtOffset(run.Length)!).GetPropertyValue(TextElement.BackgroundProperty) is SolidColorBrush brush&&brush.Color==(Color)ColorConverter.ConvertFromString(FullTextEditBuffer.ReferenceHighlightColor(dark)))text.Append(run);
                    pointer=pointer.GetPositionAtOffset(run.Length)!;
                }else pointer=pointer.GetNextContextPosition(LogicalDirection.Forward)!;
            }
            return text.ToString();
        }
        Check(HighlightedText()=="(Source)(Source)","Native reference backgrounds cover every citation and its brackets without coloring body text or spaces");
        Check(new[]{false,true}.All(d=>GraphTextColors.Palette.All(c=>c.Color!=FullTextEditBuffer.ReferenceHighlightColor(d))),"Both reference background colors differ from text color choices");
        FullTextBox.Focus();FullTextBox.Select(FullTextBox.Text.LastIndexOf("Source",StringComparison.Ordinal),6);FullTextBox.Selection.Text="Reference text";
        int nativeStart=FullTextBox.SelectionStart,nativeLength=FullTextBox.SelectionLength;Flush();
        Check(doc.Node("r")!.Title=="Reference text"&&FullTextBox.Text=="Alpha (Reference text) Beta (Reference text)","Editing the second citation updates its source and every occurrence");
        Check(FullTextBox.SelectionStart==nativeStart+8&&FullTextBox.SelectionLength==nativeLength&&FullTextBox.IsKeyboardFocusWithin,"Repeated earlier citations shift the active native selection correctly");
        Check(PropositionBox.Text=="Reference text"&&Graph.Selected.SetEquals(["r"]),"Editing a reference selects its source without leaving the current page");
        Check(HighlightedText()=="(Reference text)(Reference text)","Editing and selecting a repeated citation preserves all native background ranges. Actual: "+HighlightedText()+"; expected ranges: "+string.Join(",",fullTextBuffer!.ReferenceHighlights(dark)));
        UndoFullText(false);Flush();
        Check(doc.Node("r")!.Title=="Source"&&FullTextBox.Text=="Alpha (Source) Beta (Source)","One undo restores all occurrences of a citation");
        Check(HighlightedText()=="(Source)(Source)","Undo restores reference background boundaries");
        before=doc.Serialize();display=FullTextBox.Text;Input(display.IndexOf('('),1,"");
        Check(doc.Serialize()==before&&FullTextBox.Text==display,"Generated reference brackets cannot be deleted into source content");
        Input(5,1,"");Check(doc.Serialize()==before&&FullTextBox.Text==display,"Generated spacing cannot merge source propositions");
        FocusTextObject("a",false);Input(0,5,"正文");
        Check(FullTextBox.Text.StartsWith("正文（Source）"),"Changing body language regenerates citation punctuation");
        Check(HighlightedText()=="（Source）(Source)","Chinese reference brackets remain highlighted after editing");
        FullTextBox.Select(0,2);before=doc.Node("a")!.Title;
        var composition=new TextComposition(InputManager.Current,FullTextBox,"仮入力");
        FullTextBox.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,composition){RoutedEvent=TextCompositionManager.PreviewTextInputStartEvent});
        FullTextBox.Selection.Text="仮入力";RefreshFullText();Flush();
        Check(doc.Node("a")!.Title==before&&FullTextBox.Text.StartsWith("仮入力"),"Provisional IME text is left in the native editor until commit");
        fullTextComposing=false;EditFullText();Flush();
        Check(doc.Node("a")!.Title=="仮入力","Composition commit writes the complete text to its source");
        foreach(var language in new[]{"en","zh-CN","ja"})
        {
            Localization.Apply(Resources,language);
            const string hint="Click to edit. Changes are saved to the source proposition.";
            Check(language=="en"?Localization.Text(hint)==hint:Localization.Text(hint)!=hint,"Editing help is translated in "+language);
        }
        Localization.Apply(Resources,"en");FullTextBox.Select(0,0);Toast.Visibility=Visibility.Collapsed;UpdateLayout();
        before=doc.Serialize();
        foreach(bool isDark in new[]{false,true})
        {
            dark=isDark;ApplyTheme();RefreshFullText();UpdateLayout();
            Check(HighlightedText()==string.Concat(fullTextBuffer!.ReferenceHighlights(dark).Select(r=>FullTextBox.Text.Substring(r.Start,r.Length)))&&doc.Serialize()==before,"Theme switches update citation backgrounds without changing saved text or colors");
            var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create(Path.Combine(AppContext.BaseDirectory,"windows-full-text-edit-"+(isDark?"dark":"light")+".png"));encoder.Save(output);
        }
        Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),$"PASS: {checks} Windows full text editing checks: native text/paragraph input, citations, selection, Unicode/colors, undo/redo, source boundaries, composition deferral, autosave, reopen and three languages.\n");
    }
}
