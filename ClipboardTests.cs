using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace Papergraph;

public static class ClipboardTests
{
    sealed class MemoryClipboard : IGraphClipboardStore
    {
        public string? Text;
        public string? Read()=>Text;
        public void Write(string text)=>Text=text;
    }
    public static void Run()
    {
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        Proposition Point(string id,double x,double y,string? parent=null)=>new(){Id=id,X=x-125,Y=y-60,Parent=parent,Title="Claim "+id+" (Author, 2026)",Caption=id,Note="Notes "+id};
        var original=new GraphDocument
        {
            Nodes=[Point("a",100,100),new(){Id="g",Kind="circle",Expanded=true,X=50,Y=50,Title="Group proposition",Caption="Group title",Note="Group note",MarkColor="#E5484D",RegionMembership=new(){["inner"]=["a"]}},
                Point("b",260,220,"g"),Point("c",390,220,"g"),new(){Id="h",Kind="circle",Expanded=true,Parent="g",Title="Nested ring",X=200,Y=300},
                Point("d",280,360,"h"),Point("e",380,360,"h"),Point("shared",680,100),Point("outside",950,100)],
            Edges=[new(){Id="ag",From="a",To="g",Direction="reverse",Caption="Citation",Note="Edge note",MarkColor="#2F7DE1"},
                new(){Id="bc",From="b",To="c",Direction="both",Label="Association"},new(){Id="ch",From="c",To="h"},new(){Id="de",From="d",To="e"},
                new(){Id="as",From="a",To="shared"},new(){Id="external",From="outside",To="a"}],
            Regions=[new(){Id="frame",Title="Section title",Note="Section note",Color="#579A88",IsAbsolute=true,X=0,Y=0,Width=720,Height=650},
                new(){Id="inner",Title="Nested frame",Note="Nested notes",IsAbsolute=true,X=30,Y=30,Width=150,Height=140},
                new(){Id="overlap",IsAbsolute=true,X=650,Y=0,Width=450,Height=650},
                new(){Id="ring-frame",Parent="g",Title="Inside ring",IsAbsolute=true,X=200,Y=180,Width=260,Height=280}]
        };
        original.Validate();string sourceJson=original.Serialize();
        var fragment=GraphClipboard.Capture(original,[],["frame"]);var graph=fragment.Graph;
        Check(original.Serialize()==sourceJson,"Copy never mutates source content or geometry");
        Check(graph.Nodes.Count==8&&graph.Regions.Select(r=>r.Id).ToHashSet().SetEquals(["frame","inner","ring-frame"]),"Frame copy includes complete nested rings and frames but not a partly overlapping frame");
        Check(graph.Edges.Count==5&&graph.Edges.All(e=>e.Id!="external"),"Copy keeps all internal edges, including links to rings, without links back to external nodes");
        Check(graph.Node("g")!.Note=="Group note"&&graph.Regions.Single(r=>r.Id=="inner").Note=="Nested notes","Ring and frame writing retained");
        Check(GraphFragment.Parse(fragment.Serialize()).Serialize()==fragment.Serialize(),"Clipboard payload survives JSON round trip");

        var destination=new GraphDocument{Title="Other page",Nodes=[new(){Id="g",Kind="circle",Expanded=true,Title="Destination ring"},Point("a",50,50)]};
        var destinationJson=destination.Serialize();var target=new Point(1500,1800);
        var first=GraphClipboard.PreparePaste(destination,fragment,target,"g");
        Check(destination.Serialize()==destinationJson&&original.Serialize()==sourceJson,"Preparing paste changes neither source nor destination");
        Check(first.Bounds.TopLeft==target&&first.Bounds.Size==GraphClipboard.Bounds(graph).Size,"Paste anchors exact layout at requested world coordinates without resizing");
        var cloned=first.Document.Nodes.Where(n=>first.NodeIds.Contains(n.Id)).ToList();
        var cloneA=cloned.Single(n=>n.Caption=="a");var cloneG=cloned.Single(n=>n.Caption=="Group title");var cloneH=cloned.Single(n=>n.Title=="Nested ring");
        Check(cloneA.Parent=="g"&&cloneG.Parent=="g"&&cloneH.Parent==cloneG.Id,"Cross-page paste remaps roots to destination scope and preserves nested group parents");
        Check(first.Document.Node("a")!.Title==destination.Node("a")!.Title&&cloneA.Id!="a"&&cloneG.Id!="g","Existing IDs in the destination do not collide with copied IDs");
        var delta=target-GraphClipboard.Bounds(graph).TopLeft;
        foreach(var n in graph.Nodes)
        {
            var copy=cloned.Single(c=>c.Title==n.Title);
            Check(copy.X==n.X+delta.X&&copy.Y==n.Y+delta.Y&&copy.Caption==n.Caption&&copy.Note==n.Note&&copy.Color==n.Color&&copy.MarkColor==n.MarkColor,"Paste preserves writing, all colors, and relative positions");
        }
        var copiedEdge=first.Document.Edges.Single(e=>e.Caption=="Citation");
        Check(copiedEdge.From==cloneA.Id&&copiedEdge.To==cloneG.Id&&copiedEdge.Direction=="reverse"&&copiedEdge.MarkColor=="#2F7DE1"&&copiedEdge.Note=="Edge note","Relation endpoints, direction, mark, caption and note preserved");
        Check(cloneG.RegionMembership.Keys.All(first.RegionIds.Contains)&&cloneG.RegionMembership.Values.Single().Single()==cloneA.Id,"Legacy group membership references remapped with new object IDs");
        var second=GraphClipboard.PreparePaste(first.Document,fragment,new Point(2500,1000));
        Check(!second.NodeIds.Overlaps(first.NodeIds)&&!second.RegionIds.Overlaps(first.RegionIds),"Repeated paste creates independent objects with fresh IDs");
        var sourceIds=graph.Nodes.Select(n=>n.Id).Concat(graph.Edges.Select(e=>e.Id)).Concat(graph.Regions.Select(r=>r.Id)).ToHashSet();
        Check(!second.NodeIds.Overlaps(sourceIds),"New objects never reuse source IDs");
        var innerFragment=GraphClipboard.Capture(original,["b"],[]);
        Check(innerFragment.Graph.Nodes.Single().Parent==null&&innerFragment.Graph.Edges.Count==0,"Copying a child alone detaches it from an unselected parent");
        var mixed=GraphClipboard.Capture(original,["g","outside"],["frame","inner"]);
        Check(mixed.Graph.Nodes.Count==original.Nodes.Count&&mixed.Graph.Edges.Count==original.Edges.Count,"Mixed frame and point selection includes shared objects only once");
        var emptyFrame=new GraphDocument{Regions=[new(){Id="empty",IsAbsolute=true,Title="Empty section",Note="Preserve",Width=80,Height=80}]};
        var emptyFragment=GraphClipboard.Capture(emptyFrame,[],["empty"]);
        Check(GraphClipboard.PreparePaste(new(),emptyFragment,new Point()).Document.Regions.Single().Note=="Preserve","Empty frames can be copied and pasted");
        var board=new Rect(1000,1000,1000,1000);
        var clamped=GraphClipboard.PreparePaste(new(),fragment,new Point(9999,9999),board:board);
        Check(board.Contains(clamped.Bounds),"Paste into a frame board stays within its bounds without changing scale");
        bool rejected=false;try{GraphClipboard.PreparePaste(destination,fragment,new Point(),board:new Rect(0,0,40,40));}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&destination.Serialize()==destinationJson,"Too-small board fails without partially adding content");
        rejected=false;try{GraphFragment.Parse("{\"Format\":\"other\"}");}catch{rejected=true;}
        Check(rejected,"Unrelated clipboard formats are rejected");

        var data=Path.Combine(Path.GetTempPath(),"papergraph-clipboard-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
        Storage.Save(Path.Combine(data,"copy.papergraph"),original);File.WriteAllText(Path.Combine(data,"recent.txt"),"copy.papergraph");
        var clipboard=new MemoryClipboard();var window=new MainWindow(data){GraphClipboardStore=clipboard};var surface=window.Graph;
        surface.ClearAllSelection();surface.SelectedRegion="frame";window.SelectionChanged();
        var before=surface.Document.Serialize();
        MenuItem Item(ContextMenu menu,string title)=>menu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()==title);
        void Edit(string name)=>window.EditMenu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()==name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var menu=window.CreateGraphContext(surface,PlacementMode.MousePoint,new Point(1600,1700));
        Check(Item(menu,"Copy").IsEnabled&&!Item(menu,"Paste").IsEnabled,"Frame right-click exposes Copy and disables Paste for an empty clipboard");
        Item(menu,"Copy").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(clipboard.Text!=null&&surface.Document.Serialize()==before,"Right-click Copy writes a detached fragment without changing the graph");
        surface.ClearAllSelection();window.SelectionChanged();
        menu=window.CreateGraphContext(surface,PlacementMode.MousePoint,new Point(1600,1700));
        Item(menu,"Paste").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));var pasted=surface.Document.Serialize();
        Check(surface.SelectionBox!.Value.TopLeft==new Point(1600,1700)&&surface.SelectedRegions.Count==3,"Right-click Paste uses captured destination and selects the complete copied contents");
        Check(surface.Document.Nodes.Count==17&&surface.Document.Regions.Count==7,"Menu paste adds precisely one complete frame fragment");
        Edit("Undo");Check(surface.Document.Serialize()==before,"One undo restores the exact pre-paste graph");
        Edit("Redo");Check(surface.Document.Serialize()==pasted,"Redo restores the same pasted IDs and layout");
        Check(!window.RouteGraphClipboard(Key.C,ModifierKeys.Control,new TextBox())&&!window.RouteGraphClipboard(Key.V,ModifierKeys.Control,new TextBox()),"Text editors retain normal text copy/paste");
        Check(!window.RouteGraphClipboard(Key.V,ModifierKeys.None,surface)&&!window.RouteGraphClipboard(Key.C,ModifierKeys.Control|ModifierKeys.Shift,surface),"Unmodified V and unrelated key chords are not intercepted");
        Check(window.RouteGraphClipboard(Key.V,ModifierKeys.Control,surface,new Point(3000,3000))&&surface.Document.Nodes.Count==25,"Ctrl+V pastes the fragment at the pointer destination");
        surface.Selected=["a"];surface.SelectedRegions=[];surface.SelectionBox=new Rect(20,20,140,140);window.SelectionChanged();
        menu=window.CreateGraphContext(surface,PlacementMode.MousePoint,new Point());
        Check(Item(menu,"Copy").IsEnabled,"Explicit right-click of a marquee selection supports Copy");
        Check(window.RouteGraphClipboard(Key.C,ModifierKeys.Control,surface),"Ctrl+C handles graph selection");
        Check(GraphFragment.Parse(clipboard.Text!).Graph.Nodes.Single().Id=="a","Shortcut copies the current selection, not a previously copied frame");
        var current=surface.Document.Serialize();clipboard.Text="ordinary text";
        Check(!window.PasteGraphSelection(new Point())&&surface.Document.Serialize()==current,"Malformed clipboard leaves live content and undo history untouched");
        window.Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: frame/marquee copy, nested rings and frames, internal-only relations, cross-page/scoped paste, fresh IDs, writing/marks/layout preservation, pointer placement, fixed board bounds, single-step undo/redo, invalid clipboard atomicity and text shortcut isolation.\n");
    }
}
