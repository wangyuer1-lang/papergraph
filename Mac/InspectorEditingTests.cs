using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace Papergraph;
public partial class MainWindow
{
    async Task RunInspectorEditingTests()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        ResetDocument(new GraphDocument{Title="Inspector editing",Nodes=[new(){Id="edit-a",Title="Original body",X=30,Y=30},new(){Id="edit-b",X=400,Y=30}],Edges=[new(){Id="edit-edge",From="edit-a",To="edit-b"}],Regions=[new(){Id="edit-frame",X=30,Y=220,Width=500,Height=250,IsAbsolute=true}]},FreshPath("Inspector editing"),true);
        Graph.CancelLayout();SetFullTextVisible(true,false);
        using var pointer=new Pointer(906,PointerType.Mouse,true);
        ulong timestamp=1;
        async Task Flush(){UpdateLayout();await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);await Task.Delay(50);UpdateLayout();}
        async Task TypeByClick(TextBox box,string suffix,string label)
        {
            await Flush();Graph.Focus();
            var position=box.TranslatePoint(new Point(box.Bounds.Width/2,Math.Min(18,box.Bounds.Height/2)),this)!.Value;
            // Resolve the actual hit target: raising events directly on TextBox
            // would miss an invisible sibling intercepting real mouse input.
            var target=this.InputHitTest(position) as Control;
            bool HitsEditor()=>target!=null&&(target==box||target.GetVisualAncestors().Contains(box));
            // The composition hit-test tree updates on a render frame.
            for(var attempt=0;!HitsEditor()&&attempt<20;attempt++){await Flush();target=this.InputHitTest(position) as Control;}
            Check(HitsEditor(),label+" receives clicks through the rendered visual tree (hit "+target?.GetType().Name+")");
            target!.RaiseEvent(new PointerPressedEventArgs(target,pointer,this,position,timestamp++,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));
            var released=pointer.Captured as Control??target;
            released.RaiseEvent(new PointerReleasedEventArgs(released,pointer,this,position,timestamp++,new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
            Check(ReferenceEquals(FocusManager?.GetFocusedElement(),box),label+" gets keyboard focus after clicking");
            var before=box.Text??"";box.CaretIndex=before.Length;box.SelectionStart=box.SelectionEnd=before.Length;
            box.RaiseEvent(new TextInputEventArgs{RoutedEvent=TextInputEvent,Text=suffix});
            Check(box.Text==before+suffix,label+" accepts text input");
        }
        foreach(var isDark in new[]{false,true})
        {
            dark=isDark;ApplyTheme();Graph.ClearAllSelection();Graph.Selected=["edit-a"];SelectionChanged();
            await TypeByClick(bodyBox," 中文 日本語", "Proposition body");
            Check(doc.Node("edit-a")!.Title==bodyBox.Text,"Body input updates the selected proposition");
            await TypeByClick(captionBox," title", "Proposition title");
            Check(doc.Node("edit-a")!.Caption==captionBox.Text,"Title input updates the selected proposition");
            for(var page=0;page<4;page++)
            {
                notePages.SelectedIndex=page;
                await TypeByClick(notesBox,$" note {page+1}", $"Notes page {page+1}");
                var item=doc.Node("edit-a")!;
                Check((page==0?item.Note:item.AdditionalNotes[page-1].Body)==notesBox.Text,"Note input reaches the selected page");
            }
            Graph.ClearAllSelection();Graph.SelectedRegion="edit-frame";SelectionChanged();
            await TypeByClick(bodyBox," frame body", "Frame body");
            Check(doc.Frame("edit-frame")!.Title==bodyBox.Text,"Frame input updates the selected frame");
            Graph.ClearAllSelection();Graph.SelectedEdge="edit-edge";SelectionChanged();await Flush();
            Check(edgePanel.IsEffectivelyVisible&&!bodyBox.IsVisible,"Relation settings remain available for selected connections");
            await TypeByClick(captionBox," relation title", "Relation title");
            await TypeByClick(notesBox," relation note", "Relation notes");
            Graph.ClearAllSelection();Graph.Selected=["edit-a"];SelectionChanged();
            await TypeByClick(bodyBox," after relation", "Body after selecting a relation");
            RefreshFullText();Check(currentFullText.Text.Contains("after relation"),"Typed body changes reach the live manuscript");
        }
        Check(Save(),"Inspector edits save successfully");
        Check(GraphDocument.Parse(File.ReadAllText(file)).Serialize()==doc.Serialize(),"Typed titles, bodies and notes survive disk serialization");
    }
}
