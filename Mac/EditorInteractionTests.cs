using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace Papergraph;

public partial class MainWindow
{
    internal async Task RunEditorInteractionTests()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        async Task Flush()
        {
            UpdateLayout();
            // Native hit testing uses the rendered scene, not just the latest layout bounds.
            for(var frame=0;frame<2;frame++)
            {
                var painted=new TaskCompletionSource();
                RequestAnimationFrame(_=>painted.SetResult());
                await painted.Task;
            }
            await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
        }
        bool Hits(Control control)
        {
            var position=control.TranslatePoint(new Point(control.Bounds.Width/2,Math.Min(24,control.Bounds.Height/2)),this)!.Value;
            var hit=this.InputHitTest(position) as Visual;
            var reaches=hit==control||hit?.GetVisualAncestors().Contains(control)==true;
            if(!reaches)Console.WriteLine($"Hit test: wanted {control.GetType().Name} {control.Bounds} at {position}; reached {hit?.GetType().Name}; ancestors {string.Join(",",hit?.GetVisualAncestors().Select(v=>v.GetType().Name)??[])}");
            return reaches;
        }
        async Task Enter(TextBox box,string value)
        {
            // Animation callbacks can run before the native composition hit-test tree
            // catches up after theme/visibility changes. Wait for the actual target.
            var reachable=Hits(box);
            for(var attempt=0;!reachable&&attempt<20;attempt++)
            {
                await Task.Delay(50);await Flush();reachable=Hits(box);
            }
            Check(reachable,"Mouse input reaches "+(box==bodyBox?"proposition body":box==captionBox?"title":"notes"));
            box.Focus();box.SelectAll();
            box.RaiseEvent(new TextInputEventArgs{RoutedEvent=TextInputEvent,Text=value});
            Check(ReferenceEquals(FocusManager?.GetFocusedElement(),box)&&box.Text==value,"Focused text input updates the editor");
        }
        var fixture=new GraphDocument{Title="Editor interaction fixture",Nodes=[new(){Id="first",Title="Original",Caption="First"},new(){Id="second",Title="Next",X=400},new(){Id="ring",Kind="circle",Title="Ring text",X=700}],Edges=[new(){Id="edge",From="first",To="second"}],Regions=[new(){Id="frame",Title="Frame text",IsAbsolute=true,Width=1000,Height=800}]};
        ResetDocument(fixture,FreshPath("Editor interaction fixture"),true);Graph.CancelLayout();
        foreach(var readOnly in new[]{false,true})
        {
            SetGraphAgentReadOnly(file,readOnly);
            foreach(var showText in new[]{false,true})
            {
                SetFullTextVisible(showText,false);
                foreach(var kind in new[]{"node","edge","region","ring","node"})
                {
                    Graph.ClearAllSelection();
                    if(kind=="edge")Graph.SelectedEdge="edge";
                    else if(kind=="region")Graph.SelectedRegion="frame";
                    else Graph.Selected=[kind=="ring"?"ring":"first"];
                    SelectionChanged();await Flush();
                    var text=$"手动编辑 {kind} / {readOnly} / {showText}";
                    await Enter(captionBox,text+" title");
                    await Enter(notesBox,text+" note");
                    Check(SelectedObject()!.Note==text+" note","Personal notes accept input");
                    await Flush();
                    if(kind!="edge")
                    {
                        await Enter(bodyBox,text);
                        Check((SelectedObject() switch{Proposition n=>n.Title,Region r=>r.Title,_=>null})==text,"Body input reaches the selected graph object");
                    }
                    else Check(Hits(directionButtons.Children.OfType<Button>().First()),"Relation controls remain clickable after changing selection");
                    Check(doc.AgentReadOnly==readOnly,"Manual editing preserves the AI permission");
                }
            }
        }
        SetFullTextVisible(true,false);Graph.ClearAllSelection();Graph.Selected=["first"];SelectionChanged();await Flush();
        await Enter(bodyBox,"Click here to edit. 中文输入测试。");Save();
        Check(GraphDocument.Parse(File.ReadAllText(file)).Node("first")!.Title==bodyBox.Text,"Editor changes survive saving while AI protection is on");
    }
}
