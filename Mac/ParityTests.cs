using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using P=System.Windows.Point;
using R=System.Windows.Rect;
namespace Papergraph;
public partial class MainWindow
{
    async Task RunParityTests()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var fixture=new GraphDocument{Title="界面与操作 · Windows 对齐",Nodes=[new(){Id="p1",Caption="起点",Title="这是第一段论述。",X=0,Y=0},new(){Id="p2",Caption="结论",Title="日本語の文章も表示できます。",X=400,Y=0}],Edges=[new(){Id="e1",From="p1",To="p2"}],Regions=[new(){Id="f1",Title="研究问题",Caption="研究问题",X=-30,Y=-50,Width=730,Height=280,IsAbsolute=true,Color="#6F9DB8"}]};
        ResetDocument(fixture,FreshPath("Parity fixture"),true);Graph.ClearAllSelection();SetLanguage("zh-CN");UpdateLayout();
        var activeCategory=categoryRows.First(c=>c.Graphs.Contains(CurrentGraph));activeCategory.IsExpanded=false;libraryTree.SelectedItem=activeCategory;
        var fixtureFile=file;SwitchGraph(library.First(e=>!SamePath(e.Path,file)).Path);SwitchGraph(fixtureFile);UpdateLayout();
        Check(libraryTree.SelectedItem==CurrentGraph&&categoryRows.First(c=>c.Graphs.Contains(CurrentGraph)).IsExpanded,"Opening a graph reveals its category and selects the active file");
        Check(mainMenu.Items.OfType<MenuItem>().Count()==5,"Windows menu structure: File, Edit, Insert, View, Help");
        Check(Math.Abs(libraryTree.GetVisualRootWidth()-218)<1,"Windows library column remains 218 pixels wide");
        Check(!bodyBox.IsVisible&&emptyEditor.IsVisible&&!notesPanel.IsVisible,"Empty editor hides body and notes like Windows");
        Graph.Selected=["p1"];SelectionChanged();UpdateLayout();
        Check(bodyBox.IsVisible&&notesPanel.IsVisible&&captionPanel.IsVisible&&bodyBox.FontSize==18,"Selected proposition restores the Windows editor hierarchy");
        notePages.SelectedIndex=2;notesBox.Text="第三页测试";Graph.Selected=["p2"];SelectionChanged();Check(notePages.SelectedIndex==0,"Each object starts on its own remembered note page");Graph.Selected=["p1"];SelectionChanged();Check(notePages.SelectedIndex==2&&notesBox.Text=="第三页测试","Returning to an object restores its note page");
        Graph.ClearAllSelection();Graph.SelectedEdge="e1";SelectionChanged();Check(relationButtons.Children.Count==6&&directionButtons.Children.Count==3&&roleButtons.Children.Count==2,"Relation editor shows all six markers, three directions and Body/Reference choices");
        var edges=doc.Edges.Count;AddEdge("p1","p2");Check(doc.Edges.Count==edges&&Graph.SelectedEdge=="e1","Creating the same support arrow selects it without duplicating it");Graph.CancelLayout();
        SetTextRole("e1","reference");Check(!orderPanel.IsVisible,"Reference links hide branch order");SetTextRole("e1","flow");
        Graph.SelectedEdge=null;Graph.Selected=["p1"];SelectionChanged();Graph.SetView(.9,new P(120,100));Graph.DetailReturnView=(.7,new P(30,40));var before=CaptureBoard();EnterBoard(null,"f1");Leave();Check(Graph.Selected.SetEquals(before.Selected)&&Graph.Zoom==before.Zoom&&Graph.Offset==before.Offset&&Graph.DetailReturnView==before.Detail,"Leaving a board restores selection, viewport and detail-return state");
        var savedFixture=doc.Serialize();
        var navigation=new GraphDocument{Nodes=[new(){Id="center",X=325,Y=390,Title="center"},new(){Id="local-up",X=325,Y=290,Title="local-up"},new(){Id="next",X=325,Y=90,Title="next"}],Regions=[new(){Id="current",X=300,Y=300,Width=300,Height=300,IsAbsolute=true},new(){Id="next-frame",X=300,Y=0,Width=300,Height=220,IsAbsolute=true}]};
        doc=navigation;Graph.Document=doc;Graph.ClearAllSelection();Graph.Selected=["center"];SelectionChanged();ResetFrameTap();ArrowKey(Key.Up,false,1000);Check(Graph.Selected.SetEquals(["local-up"]),"One arrow stays within the current frame");ArrowKey(Key.Up,false,1180);Check(Graph.Selected.SetEquals(["next"]),"Double-tapping an arrow jumps to the next frame");
        doc=GraphDocument.Parse(savedFixture);Graph.Document=doc;Graph.ClearAllSelection();Graph.Selected=["p1"];SelectionChanged();ResetFrameTap();
        var oldFile=file;var count=doc.Nodes.Count;Graph.Focus();Keys(this,new KeyEventArgs{Key=Key.N,KeyModifiers=KeyModifiers.Meta});Graph.CancelLayout();Check(file==oldFile&&doc.Nodes.Count==count+1,"Command-N adds a proposition instead of creating a new document");
        var inserted=editorId;Escape(true);Check(ReferenceEquals(FocusManager?.GetFocusedElement(),Graph)&&editorId==inserted,"Escape from text editing returns focus without clearing selection");Undo();
        Graph.ClearAllSelection();Graph.Selected=["p1","p2"];Graph.SelectionBox=new R(-20,-20,700,200);SelectionChanged();Check(multiBar.IsVisible&&frameButton.IsEnabled&&circleButton.IsEnabled,"Box selection exposes frame, ring and delete bottom actions");
        var box=Graph.SelectionBox;FrameAroundSelection();Check(Graph.SelectedRegion!=null&&GraphBoard.Bounds(doc.Frame(Graph.SelectedRegion)!)==box,"Frame creation respects the original selection rectangle");Undo();
        Graph.Selected=["p1"];SelectionChanged();SetFullTextVisible(true,false);UpdateLayout();
        Check(graphArea.IsVisible&&textArea.IsVisible&&workspace.RowDefinitions[2].Height.Value==300,"Live full text uses a resizable 300-pixel bottom panel");
        FocusTextObject("p2");Check(fullTextVisible&&editorId=="p2"&&Graph.Selected.Contains("p2"),"Clicking manuscript source keeps both graph and manuscript visible");
        bodyBox.Text="日本語の文章も表示できます。";RefreshFullText();Check(currentFullText.Text.Contains("日本語"),"Live full text follows source edits");
        Graph.Selected=["p1"];SelectionChanged();notePages.SelectedIndex=0;Graph.ShowCaptions=true;Graph.Fit(false);toast.IsVisible=false;Save();UpdateLayout();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},Avalonia.Threading.DispatcherPriority.Background);
        Check(libraryTree.SelectedItem==CurrentGraph&&libraryTree.GetVisualDescendants().OfType<TreeViewItem>().Any(c=>c.DataContext==CurrentGraph&&c.IsSelected),"The active library graph stays visibly selected after autosave and refresh");
        Check(libraryTree.ItemCount>0&&libraryTree.Bounds.Height>0&&bodyBox.Bounds.Height>notesBox.Bounds.Height,"Library rows and the two-thirds body / one-third notes layout are measured");
        void Screenshot(string name){UpdateLayout();using var bitmap=new RenderTargetBitmap(new PixelSize((int)Width,(int)Height),new Vector(96,96));bitmap.Render(this);bitmap.Save(Path.Combine(dataDir,name));}
        Screenshot("parity-light.png");dark=true;ApplyTheme();Screenshot("parity-dark.png");dark=false;ApplyTheme();
        Graph.ClearAllSelection();Graph.SelectedEdge="e1";SelectionChanged();Screenshot("parity-relation.png");Graph.SelectedEdge=null;Graph.Selected=["p1"];SelectionChanged();SaveSettings();
    }
}
internal static class ParityLayoutExtensions
{
    internal static double GetVisualRootWidth(this Control c)=>(c.Parent?.Parent as Control)?.Bounds.Width??0;
}
