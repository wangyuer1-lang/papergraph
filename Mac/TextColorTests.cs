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
    internal async Task RunTextColorTests()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        async Task Flush(){UpdateLayout();await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);UpdateLayout();}
        var n=new Proposition{Id="one",Title="甲乙🌱丙丁重複表現"};
        n.BodyColors=GraphTextColors.Apply(n.Title,null,2,2,"#D44747");
        n.Title="序"+n.Title;
        Check(n.BodyColors!.Single()==new BodyTextColor(3,2,"#D44747"),"Inserting before a marked emoji shifts its UTF-16 range");
        n.Title=n.Title.Replace("🌱","中文");
        Check(n.BodyColors!.Single()==new BodyTextColor(3,2,"#D44747"),"Replacing marked text preserves its color");
        n.BodyColors=GraphTextColors.Apply(n.Title,n.BodyColors,4,3,"#397FC4");
        Check(n.BodyColors!.SequenceEqual(new[]{new BodyTextColor(3,1,"#D44747"),new BodyTextColor(4,3,"#397FC4")}),"Overlapping colors split the old range without changing other text");
        n.BodyColors=GraphTextColors.Apply(n.Title,n.BodyColors,5,1,null);
        Check(n.BodyColors!.Count==3,"Clearing part of a color preserves both remaining fragments");
        n.Title=n.Title.Remove(3,1);
        Check(n.BodyColors!.All(c=>c.Color!="#D44747")&&n.BodyColors[0].Start==3,"Deleting marked text removes its color and moves following ranges");
        var fixture=new GraphDocument{Title="Text colors",Nodes=[n,new(){Id="two",Title="Second proposition",X=400}],Edges=[new(){Id="flow",From="one",To="two"}]};
        var saved=fixture.Serialize();Check(GraphDocument.Parse(saved).Serialize()==saved,"Text colors survive document serialization");
        var copy=GraphClipboard.PreparePaste(new(),GraphClipboard.Capture(fixture,["one"],[]),new System.Windows.Point(0,0));
        Check(copy.Document.Nodes[0].BodyColors!.SequenceEqual(n.BodyColors!),"Graph copying preserves color ranges");
        bool invalid=false;try{GraphTextColors.Validate("🌱",[new(1,1,"#D44747")]);}catch(InvalidDataException){invalid=true;}
        Check(invalid,"Color ranges cannot split a surrogate pair");
        invalid=false;try{GraphTextColors.Validate("abc",[new(0,int.MaxValue,"#D44747")]);}catch(InvalidDataException){invalid=true;}
        Check(invalid,"Out-of-bounds color ranges are rejected");
        ResetDocument(fixture,FreshPath("Text colors"),true);Graph.CancelLayout();Graph.Selected=["one"];SelectionChanged();SetFullTextVisible(true,false);await Flush();
        var body=doc.Node("one")!.Title;var plain=GraphFullText.Build(doc).Text;
        var presenter=bodyBox.GetVisualDescendants().OfType<BodyTextPresenter>().Single();
        var local=new Point(bodyBox.Bounds.Width/2,20);var p=bodyBox.TranslatePoint(local,this)!.Value;
        var hit=this.InputHitTest(p) as Visual;Check(hit==bodyBox||hit?.GetVisualAncestors().Contains(bodyBox)==true,"Colored editor still receives pointer input");
        bodyBox.Focus();bodyBox.SelectionStart=1;bodyBox.SelectionEnd=3;await Flush();
        Check(textColorButtons.All(b=>b.IsEnabled),"Selecting text enables color controls");
        textColorButtons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));await Flush();
        Check(bodyBox.SelectionStart==1&&bodyBox.SelectionEnd==3&&doc.Node("one")!.Title==body,"Color button preserves the selection and exact source text");
        Check(doc.Node("one")!.BodyColors!.Any(c=>c.Start==1&&c.Length==2&&c.Color=="#D44747"),"Color button applies only to selected text");
        Check(plain==GraphFullText.Build(doc).Text,"Colors do not change full text or character counts");
        Check(fullTextBuffer!.Colors(doc).Any(r=>r.Color=="#D44747"),"Full text retains source color ranges");
        var colored=doc.Node("one")!.BodyColors!.ToArray();
        // Selected text uses the platform selection foreground; inspect after clearing selection.
        bodyBox.SelectionEnd=bodyBox.SelectionStart;await Flush();
        Check(presenter.TextLayout.TextLines.SelectMany(l=>l.TextRuns).Any(r=>r.Properties?.ForegroundBrush is SolidColorBrush b&&b.Color==Color.Parse("#D44747")),"Native editable text layout renders the saved color");
        bodyBox.SelectAll();bodyBox.RaiseEvent(new TextInputEventArgs{RoutedEvent=TextInputEvent,Text="新正文 中文入力 🌱"});await Flush();
        Check(doc.Node("one")!.Title=="新正文 中文入力 🌱","Colored editor accepts Chinese and Unicode input");
        UndoBody(false);await Flush();Check(doc.Node("one")!.Title==body&&doc.Node("one")!.BodyColors!.SequenceEqual(colored),"Undo restores both edited text and its original colors");
        UndoBody(true);await Flush();Check(doc.Node("one")!.Title=="新正文 中文入力 🌱","Redo restores edited text");UndoBody(false);await Flush();
        bodyBox.SelectionStart=1;bodyBox.SelectionEnd=3;ColorBodySelection(null);await Flush();
        Check(doc.Node("one")!.BodyColors?.All(c=>c.Color!="#D44747")!=false,"Clear color removes selected formatting");UndoBody(false);await Flush();
        Save();Check(GraphDocument.Parse(File.ReadAllText(file)).Node("one")!.BodyColors!.SequenceEqual(colored),"Colors persist to disk");
        Graph.Selected=["two"];SelectionChanged();await Flush();Graph.Selected=["one"];SelectionChanged();await Flush();
        Check(presenter.TextLayout.TextLines.SelectMany(l=>l.TextRuns).Any(r=>r.Properties?.ForegroundBrush is SolidColorBrush b&&b.Color==Color.Parse("#D44747")),"Switching propositions restores the correct colors");
        presenter.PreeditText="输入";await Flush();Check(presenter.TextLayout.TextLines.Count>0,"Native IME preedit remains usable with colored text");presenter.PreeditText=null;
        JsonElement Call(object value)=>JsonSerializer.SerializeToElement(HandleAgent(JsonSerializer.SerializeToElement(value)),GraphDocument.Options);
        var request=new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),nodes=new[]{new{id="one",bodyColors=new[]{new{start=0,length=2,color="#9A63BE",text=body[..2]}}}}};
        Call(request);Check(doc.Node("one")!.BodyColors!.Single().Color=="#9A63BE"&&doc.Node("one")!.Title==body,"Agent can atomically color exact text without editing the proposition");Call(request);
        Check(File.ReadAllText(file)==doc.Serialize(),"Agent coloring is durable and idempotent");
        invalid=false;try{Call(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision="stale",nodes=new[]{new{id="one",bodyColors=Array.Empty<object>()}}});}catch(InvalidDataException){invalid=true;}Check(invalid,"Stale coloring requests are rejected");
        var mismatchBefore=doc.Serialize();invalid=false;try{Call(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),nodes=new[]{new{id="one",bodyColors=new[]{new{start=0,length=2,color="#9A63BE",text="wrong"}}}}});}catch(InvalidDataException){invalid=true;}Check(invalid&&doc.Serialize()==mismatchBefore,"Mismatched text selections reject the entire agent batch");
        SetGraphAgentReadOnly(file,true);invalid=false;try{Call(new{operation="editGraph",requestId=Guid.NewGuid().ToString(),expectedDocument=file,expectedRevision=AgentProtocol.Revision(doc),nodes=new[]{new{id="one",bodyColors=Array.Empty<object>()}}});}catch(InvalidDataException){invalid=true;}Check(invalid,"AI protection rejects agent formatting");
        Graph.Selected=["one"];SelectionChanged();bodyBox.SelectionStart=0;bodyBox.SelectionEnd=2;ColorBodySelection("#D44747");Check(doc.Node("one")!.BodyColors!.Single().Color=="#D44747","Human formatting remains available on AI-protected graphs");
        foreach(var theme in new[]{false,true}){dark=theme;ApplyTheme();await Flush();using var bitmap=new RenderTargetBitmap(new PixelSize((int)Width,(int)Height),new Vector(96,96));bitmap.Render(this);bitmap.Save(Path.Combine(dataDir,"text-colors-"+(theme?"dark":"light")+".png"));}
        Console.WriteLine("Text color evidence: "+dataDir);
    }
}
