using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace Papergraph;

public sealed partial class GraphSurface : FrameworkElement
{
    internal const double MinimumZoom=1e-9,EditingMinimumZoom=.10,MaximumZoom=3.2;
    GraphDocument document=new();string? scope,boardRegion;
    public GraphDocument Document {get=>document;set{CancelLayout();document=value;RefreshData();}}
    public string? Scope {get=>scope;set=>SetBoard(value,null);}
    public string? BoardRegion=>boardRegion;
    public Rect? BoardBounds=>boardRegion!=null&&document.Regions.FirstOrDefault(r=>r.Id==boardRegion&&r.Parent==scope) is Region r?GraphBoard.Bounds(r):null;
    public IReadOnlyList<Proposition> VisibleNodes=>visible;
    public IReadOnlyList<Region> VisibleRegions=>regions;
    public IEnumerable<(string From,string To)> VisibleLinks=>projected.Select(e=>(e.From,e.To));
    public bool MoveRegionContents {get;set;}
    HashSet<string> selected=[];
    public HashSet<string> Selected {get=>selected;set{selected=value;SelectionBox=null;}}
    public HashSet<string> SelectedRegions {get;set;}=[];
    public Rect? SelectionBox {get;set;}
    public int SelectionCount=>Selected.Count+SelectedRegions.Count;
    public bool GroupSelection=>SelectionBox!=null||SelectionCount>1;
    public string? SelectedEdge {get;set;}
    public string? SelectedRegion {get=>SelectedRegions.Count==1?SelectedRegions.First():null;set{SelectedRegions=value==null?[]:[value];SelectionBox=null;}}
    public bool LinkMode {get;set;}
    public string? LinkStart {get;set;}
    public bool Dark {get;private set;}
    public bool IsPreview {get;set;}
    public bool DrawingRegion {get;set;}
    bool showCaptions;
    public bool ShowCaptions {get=>showCaptions;set{if(showCaptions==value)return;showCaptions=value;captions.Clear();captionsDirty=true;InvalidateVisual();}}
    ConnectionDisplay connectionDisplay;
    HashSet<string>? allowedEdges;
    public ConnectionDisplay ConnectionDisplay
    {
        get=>connectionDisplay;
        set
        {
            if(!Enum.IsDefined(value))throw new ArgumentOutOfRangeException(nameof(value));
            if(connectionDisplay==value)return;
            connectionDisplay=value;UpdateConnectionFilter();hoverEdge=null;
            captions.Clear();captionsDirty=geometryDirty=true;InvalidateVisual();
            if(SelectedEdge!=null&&allowedEdges!=null&&!allowedEdges.Contains(SelectedEdge)){SelectedEdge=null;AnnounceSelection();}
        }
    }
    void UpdateConnectionFilter()=>allowedEdges=connectionDisplay==ConnectionDisplay.All?null:GraphConnections.MatchingIds(document,connectionDisplay);
    internal IReadOnlyList<string> DrawnEdgeIds {get{BuildGeometry();return paths.Select(p=>p.Edge.Id).ToArray();}}
    internal string? HitRelation(Point world)=>HitEdge(world)?.Edge.Id;
    public double Zoom {get;private set;}=1;
    public Point Offset {get;private set;}=new(50,50);
    public double RightInset {get;set;}
    public bool IsInteracting=>dragging||panning||lasso||linkDragging||drawingBox;
    double UsableWidth=>Math.Max(180,ActualWidth-RightInset);
    public event Action? SelectionChanged,BeforeChange,Changed,ViewChanged,EditRequested,ContextRequested;
    public event Action<string>? EnterCircle,EnterRegion;
    public event Action? LayoutFinished;
    public event Action<string>? LayoutFailed;
    public event Action<string>? LayoutNotice;
    public event Action<Point>? CreateNode;
    public event Action<Rect>? CreateRegion;
    public event Action<Rect>? BoxSelectionCompleted;
    public event Action<string,string>? Connect;
    readonly Dictionary<string,SolidColorBrush> nodeBrushes=[];
    Dictionary<string,Proposition> index=[];
    List<Proposition> visible=[];List<Region> regions=[];
    List<(Relation Edge,string From,string To,double Bend)> projected=[];
    readonly List<EdgePath> paths=[];
    readonly Dictionary<string,(string Text,double Dpi,FormattedText Layout)> captionText=[];
    readonly List<(GraphCaptions.Placement Placement,FormattedText Text)> captions=[];
    readonly Dictionary<string,int> captionSlots=[];
    bool captionsDirty=true;
    bool captionReconsider=true;
    (double Zoom,Point Offset,Size Viewport,double Dpi)? captionView;
    Brush captionBrush=GraphStyle.Brush("#526479");
    bool geometryDirty=true;double geometryZoom=-1;
    string? hoverId,hoverEdge,hoverRegion,linkTarget;
    Brush background=GraphStyle.Brush("#F7F8FA"),edgeBrush=GraphStyle.Brush("#65788F"),accent=GraphStyle.Brush("#356FBD"),regionBrush=GraphStyle.Brush("#AAB8C9");
    Point down,last,linkEnd;bool dragging,panning,lasso,moved,linkDragging,rightGesture,drawingBox,resizingRegion;
    Region? movingRegion;Rect originalRegion,dragBounds;RegionMove? regionMove;SelectionMove? selectionMove;Rect? boxBeforeMove;bool movingContents;GraphSpacing.DragLimit[] dragLimits=[];Vector previousDrag;
    Rect? selectionRect;HashSet<string> lassoBase=[],regionLassoBase=[],selectionBeforeBox=[],regionsBeforeBox=[];Point boxStart;string? edgeBeforeBox;Rect? previousSelectionBox;bool boxAdditive;
    double targetZoom=1;Point targetOffset=new(50,50);bool animating;long lastFrame;
    (double Zoom,Point Offset)? detailView;
    CancellationTokenSource? layoutCancellation;long layoutRevision;
    bool regionGeometryDirty=true;List<GraphRegionColors.Patch> intersectionPatches=[];
    internal (double Zoom,Point Offset)? DetailReturnView {get=>detailView;set=>detailView=value;}
    sealed record EdgePath(Relation Edge,string From,string To,StreamGeometry Geometry,Point[] Samples,Point Start,Point Control,Point End,Rect Bounds,double VisibleLength,double StartRadius,double EndRadius);
    public GraphSurface()
    {
        Focusable=true;ClipToBounds=true;FocusVisualStyle=null;InputMethod.SetIsInputMethodEnabled(this,false);
        SizeChanged+=(s,e)=>{if(e.PreviousSize.Width>0&&e.PreviousSize.Height>0){var delta=new Vector((e.NewSize.Width-e.PreviousSize.Width)/2,(e.NewSize.Height-e.PreviousSize.Height)/2);Offset+=delta;targetOffset+=delta;}InvalidateVisual();ViewChanged?.Invoke();};
        Unloaded+=(s,e)=>{StopAnimation();CancelLayout();};
    }
    public static SolidColorBrush Brush(string value)=>GraphStyle.Brush(value);
    public static Rect Bounds(Proposition node)=>GraphStyle.Bounds(node);
    public void ApplyTheme(bool dark)
    {
        Dark=dark;background=Brush(dark?"#191C22":"#F7F8FA");edgeBrush=Brush(dark?"#9AABC1":"#65788F");accent=Brush(dark?"#9CC2FF":"#356FBD");regionBrush=Brush(dark?"#8292A9":"#97A6B9");captionBrush=Brush(dark?"#BDC8D7":"#526479");captionText.Clear();captionsDirty=true;nodeBrushes.Clear();InvalidateVisual();
    }
    public void RefreshData()
    {
        CancelLayout();
        index=document.Nodes.ToDictionary(n=>n.Id);var exposed=document.Descendants(document.Visible(scope).Select(n=>n.Id));
        visible=document.Nodes.Where(n=>exposed.Contains(n.Id)).ToList();regions=document.Regions.Where(r=>r.Parent==scope||r.Parent!=null&&exposed.Contains(r.Parent)).ToList();
        if(boardRegion!=null){var board=regions.FirstOrDefault(r=>r.Id==boardRegion);if(board==null)boardRegion=null;else{exposed=document.Descendants(GraphBoard.Members(document,board).Select(n=>n.Id));visible=document.Nodes.Where(n=>exposed.Contains(n.Id)).ToList();var inner=GraphBoard.InnerRegions(document,board).Select(r=>r.Id).ToHashSet();regions=document.Regions.Where(r=>inner.Contains(r.Id)||r.Parent!=null&&exposed.Contains(r.Parent)).ToList();}}
        var visibleIds=visible.Select(n=>n.Id).Concat(regions.Select(r=>r.Id)).ToHashSet();if(hoverId!=null&&!visibleIds.Contains(hoverId))hoverId=null;hoverEdge=null;
        string? Project(string id)=>visibleIds.Contains(id)?id:null;
        var edges=new List<(Relation Edge,string From,string To)>();foreach(var edge in document.Edges){var from=Project(edge.From);var to=Project(edge.To);if(from!=null&&to!=null&&from!=to&&visibleIds.Contains(from)&&visibleIds.Contains(to))edges.Add((edge,from,to));}
        projected=[];foreach(var group in edges.GroupBy(e=>string.CompareOrdinal(e.From,e.To)<0?e.From+"|"+e.To:e.To+"|"+e.From)){var list=group.ToList();for(int i=0;i<list.Count;i++)projected.Add((list[i].Edge,list[i].From,list[i].To,(i-(list.Count-1)/2d)*28));}
        var captionIds=document.Nodes.Select(n=>n.Id).Concat(document.Edges.Select(e=>e.Id)).Concat(document.Regions.Select(r=>r.Id)).ToHashSet();
        foreach(var id in captionSlots.Keys.Where(id=>!captionIds.Contains(id)).ToArray())captionSlots.Remove(id);
        captionText.Clear();captions.Clear();geometryDirty=regionGeometryDirty=true;InvalidateVisual();
    }
    public void ContentChanged(){CancelLayout();geometryDirty=true;InvalidateVisual();}
    public void SetBoard(string? parent,string? region){CancelLayout();scope=parent;boardRegion=region;detailView=null;RefreshData();}
    public Point ClampToBoard(Point point,double radius=20)=>BoardBounds is Rect bounds?GraphBoard.Clamp(point,bounds,radius+6):point;
    public void CancelLayout(){layoutRevision++;layoutCancellation?.Cancel();layoutCancellation=null;}
    public void StartRelaxation(IEnumerable<string> seeds,IEnumerable<string>? pinned=null)
    {
        CancelLayout();if(IsPreview||visible.Count<2)return;var seedSet=seeds.ToHashSet();var pinnedSet=pinned?.ToHashSet()??[];var sources=visible.Where(n=>seedSet.Contains(n.Id)).Select(GraphStyle.Center).ToArray();if(sources.Length==0)return;
        double Distance(Proposition n)=>sources.Min(p=>(GraphStyle.Center(n)-p).Length);
        var linked=projected.Where(e=>seedSet.Contains(e.From)||seedSet.Contains(e.To)).SelectMany(e=>new[]{e.From,e.To}).ToHashSet();
        var candidates=visible.Where(n=>n.Kind!="circle"&&(Distance(n)<520||linked.Contains(n.Id))).OrderBy(n=>seedSet.Contains(n.Id)?-1:Distance(n)).Take(220).ToArray();
        var active=candidates.Where(n=>Distance(n)<310||linked.Contains(n.Id)).Take(130).Select(n=>n.Id).ToHashSet();
        Rect? Limits(Proposition n){Rect? limit=BoardBounds;var center=GraphStyle.Center(n);foreach(var region in document.Regions.Where(r=>r.Parent==scope&&r.IsAbsolute&&GraphBoard.Bounds(r).Contains(center))){var box=GraphBoard.Bounds(region);if(limit is Rect prior){prior.Intersect(box);limit=prior;}else limit=box;}return limit;}
        var bodies=candidates.Select(n=>new GraphRelaxation.Body(n.Id,GraphStyle.Center(n),GraphStyle.Radius(n)+(n.Kind=="circle"?7:0),active.Contains(n.Id)&&!pinnedSet.Contains(n.Id),Limits(n),seedSet.Contains(n.Id))).ToArray();
        var layout=new GraphRelaxation(bodies,VisibleLinks.ToArray());layout.Complete();ApplyLayout(layout);
    }
    public async void ArrangeNaturally()
    {
        await ArrangeInsideAsync();
    }
    internal async Task ArrangeInsideAsync()
    {
        CancelLayout();if(IsPreview||IsInteracting||visible.Count<2)return;
        var snapshot=GraphDocument.Parse(document.Serialize());var currentScope=scope;var currentRegion=boardRegion;
        var selectedNodes=Selected.ToArray();var selectedRegions=SelectedRegions.ToArray();var stamp=layoutRevision;
        using var cancel=new CancellationTokenSource();layoutCancellation=cancel;
        try
        {
            var layout=await Task.Run(()=>GraphArrange.Compute(snapshot,currentScope,currentRegion,selectedNodes,selectedRegions,cancel.Token),cancel.Token);
            if(stamp!=layoutRevision||cancel.IsCancellationRequested)return;
            layoutCancellation=null;
            if(layout.Positions.Count>0)
            {
                BeforeChange?.Invoke();
                foreach(var (id,p) in layout.Positions)if(index.TryGetValue(id,out var node)){node.X=p.X;node.Y=p.Y;}
                geometryDirty=regionGeometryDirty=true;InvalidateVisual();LayoutFinished?.Invoke();
            }
            LayoutNotice?.Invoke(layout.Crowded>0?$"Arranged inside · {layout.Crowded} area(s) kept in place: more room needed":layout.Positions.Count>0?"Arranged inside · Ctrl+Z to undo":"Already arranged · select a □ or ◎ to arrange its contents");
        }
        catch(OperationCanceledException){}catch(Exception ex){LayoutFailed?.Invoke(ex.Message);}
        finally{if(ReferenceEquals(layoutCancellation,cancel))layoutCancellation=null;}
    }
    void ApplyLayout(GraphRelaxation layout)
    {
        bool changed=false;for(int i=0;i<layout.Bodies.Count;i++)if(index.TryGetValue(layout.Bodies[i].Id,out var n)){var delta=layout.Positions[i]-GraphStyle.Center(n);if(delta.Length>.00001){n.X+=delta.X;n.Y+=delta.Y;changed=true;}}
        geometryDirty=true;InvalidateVisual();if(changed)LayoutFinished?.Invoke();
    }
    public void RefreshSelection()=>InvalidateVisual();
    void AnnounceSelection(){SelectionChanged?.Invoke();InvalidateVisual();}
    public Point ToWorld(Point point)=>new((point.X-Offset.X)/Zoom,(point.Y-Offset.Y)/Zoom);
    public Point ToScreen(Point point)=>new(point.X*Zoom+Offset.X,point.Y*Zoom+Offset.Y);
    public Rect RegionBounds(Region region){if(region.IsAbsolute)return new Rect(region.X,region.Y,region.Width,region.Height);var b=Rect.Empty;foreach(var id in region.Members)if(index.TryGetValue(id,out var n))b.Union(Bounds(n));if(b.IsEmpty)return b;b.Inflate(28,28);return b;}
    public void SetView(double zoom,Point offset,bool smooth=false){targetZoom=Math.Clamp(zoom,MinimumZoom,MaximumZoom);targetOffset=offset;if(smooth)StartAnimation();else{StopAnimation(false);Zoom=targetZoom;Offset=targetOffset;InvalidateVisual();ViewChanged?.Invoke();}}
    public void ZoomBy(double factor,Point? anchor=null)
    {
        var p=anchor??new Point(UsableWidth/2,ActualHeight/2);var world=ToWorld(p);targetZoom=Math.Clamp(targetZoom*factor,MinimumZoom,MaximumZoom);targetOffset=new Point(p.X-world.X*targetZoom,p.Y-world.Y*targetZoom);StartAnimation();
    }
    void StartAnimation(){if(animating)return;animating=true;lastFrame=Stopwatch.GetTimestamp();CompositionTarget.Rendering+=Animate;}
    void StopAnimation(bool resetTarget=true){if(animating){CompositionTarget.Rendering-=Animate;animating=false;}if(resetTarget){targetOffset=Offset;targetZoom=Zoom;}}
    void Animate(object? sender,EventArgs args)
    {
        var now=Stopwatch.GetTimestamp();var elapsed=(now-lastFrame)/(double)Stopwatch.Frequency;lastFrame=now;AdvanceView(elapsed);
    }
    internal void AdvanceView(double elapsed)
    {
        var t=1-Math.Exp(-Math.Clamp(elapsed,0,.05)*24);Zoom+=(targetZoom-Zoom)*t;Offset+= (targetOffset-Offset)*t;
        if(Math.Abs(Zoom-targetZoom)<Math.Max(1e-12,targetZoom*.0008)&&(Offset-targetOffset).Length<.18){Zoom=targetZoom;Offset=targetOffset;StopAnimation(false);}InvalidateVisual();ViewChanged?.Invoke();
    }
    internal Rect ContentBounds(double zoom)
    {
        var bounds=BoardBounds??Rect.Empty;
        var boxes=visible.ToDictionary(n=>n.Id,n=>n.Kind=="circle"?GraphGroups.Bounds(document,n,zoom):GraphGroups.PointBounds(n,zoom));
        foreach(var box in boxes.Values)bounds.Union(box);
        foreach(var region in regions){var box=RegionBounds(region);bounds.Union(box);boxes[region.Id]=box;}
        // Include parallel relation lanes as well as their endpoints.
        foreach(var edge in projected)
        {
            var from=boxes[edge.From];var to=boxes[edge.To];
            var a=new Point(from.X+from.Width/2,from.Y+from.Height/2);var b=new Point(to.X+to.Width/2,to.Y+to.Height/2);
            var vector=b-a;if(vector.Length<.001)vector=new Vector(1,0);
            var normal=new Vector(-vector.Y,vector.X);normal.Normalize();if(string.CompareOrdinal(edge.From,edge.To)>0)normal=-normal;
            bounds.Union(GraphCurve.Create(a,b,normal,edge.Bend).Control);
        }
        return bounds;
    }
    public void Fit(bool smooth=true,bool editing=false)
    {
        detailView=null;
        var b=ContentBounds(1);if(b.IsEmpty){SetView(1,new Point(UsableWidth/2,ActualHeight/2),smooth);return;}
        var width=Math.Max(60,UsableWidth-180);var height=Math.Max(60,ActualHeight-160);
        var minimum=editing?EditingMinimumZoom:MinimumZoom;
        double Scale(Rect bounds)=>Math.Clamp(Math.Min(width/Math.Max(bounds.Width,160),height/Math.Max(bounds.Height,160)),minimum,1.35);
        var zoom=Scale(b);
        // Points and rings keep screen-space padding. Fit their bounds at the destination scale,
        // starting from the same geometry every time so repeated Fit does not drift.
        for(int i=0;i<32;i++)
        {
            b=ContentBounds(zoom);var next=Math.Min(zoom,Scale(b));
            if(next>=zoom*(1-1e-8))break;
            zoom=next;
        }
        b=ContentBounds(zoom);
        var center=new Point(b.X+b.Width/2,b.Y+b.Height/2);
        SetView(zoom,new Point(UsableWidth/2-center.X*zoom,ActualHeight/2-center.Y*zoom),smooth);
    }
    public void ToggleDetail(Point? pointer=null,bool smooth=true)
    {
        if(IsPreview||IsInteracting)return;if(detailView is { } prior){detailView=null;SetView(prior.Zoom,prior.Offset,smooth);return;}
        detailView=(Zoom,Offset);var bounds=SelectedBounds();
        if(bounds.IsEmpty&&SelectedEdge!=null){BuildGeometry();var edge=paths.FirstOrDefault(e=>e.Edge.Id==SelectedEdge);if(edge!=null)bounds=new Rect(edge.Start,edge.End);}
        var center=bounds.IsEmpty?ToWorld(pointer??new Point(UsableWidth/2,ActualHeight/2)):new Point(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2);
        var zoom=bounds.IsEmpty?1.2:Math.Clamp(Math.Min((UsableWidth-140)/Math.Max(100,bounds.Width),(ActualHeight-140)/Math.Max(100,bounds.Height)),.6,1.5);
        SetView(Math.Max(Zoom,zoom),new Point(UsableWidth/2-center.X*Math.Max(Zoom,zoom),ActualHeight/2-center.Y*Math.Max(Zoom,zoom)),smooth);
    }
    public void EnsureVisible(string id){if(IsInteracting||!index.TryGetValue(id,out var n))return;var p=ToScreen(ObjectCenter(n));var safe=new Point(Math.Clamp(p.X,70,Math.Max(70,UsableWidth-70)),Math.Clamp(p.Y,70,Math.Max(70,ActualHeight-70)));if((safe-p).Length>1)SetView(Zoom,Offset+(safe-p),true);}
    string? navigationFrame,navigationLastNode;
    internal bool NavigateNodes(Vector direction)
    {
        if(IsPreview||IsInteracting||DrawingRegion||LinkMode||visible.Count==0||direction.LengthSquared<.001)return false;
        direction.Normalize();var chosen=visible.Where(n=>Selected.Contains(n.Id)).ToArray();Point? origin=null;
        if(chosen.Length>0){var bounds=Rect.Empty;foreach(var node in chosen)bounds.Union(ObjectCenter(node));origin=new Point(bounds.X+bounds.Width*(1+direction.X)/2,bounds.Y+bounds.Height*(1+direction.Y)/2);}
        if(origin==null&&SelectedEdge!=null){BuildGeometry();var edge=paths.FirstOrDefault(p=>p.Edge.Id==SelectedEdge);if(edge!=null)origin=Curve(edge,.5);}
        if(origin==null&&SelectedRegions.Count>0){var bounds=SelectedBounds();if(!bounds.IsEmpty)origin=new Point(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2);}
        // Keep the starting frame throughout a keyboard sequence, including overlap areas.
        bool InFrame(Proposition node,Region frame)=>GraphBoard.Bounds(frame).Contains(ObjectCenter(node));
        Region? frame=null;
        if(chosen.Length==1&&chosen[0].Id==navigationLastNode&&navigationFrame!=null)
            frame=regions.FirstOrDefault(r=>r.Id==navigationFrame&&InFrame(chosen[0],r));
        if(frame==null&&origin is Point start)
            frame=regions.Where(r=>chosen.Length>0?chosen.All(n=>InFrame(n,r)):SelectedRegions.Count>0?SelectedRegions.Contains(r.Id):GraphBoard.Bounds(r).Contains(start))
                .OrderBy(r=>r.Width*r.Height).ThenBy(r=>r.Id,StringComparer.Ordinal).FirstOrDefault();
        var candidates=visible.Where(n=>frame!=null?InFrame(n,frame):origin==null||!regions.Any(r=>InFrame(n,r))).ToArray();
        Proposition? next;
        if(origin is Point anchor)
        {
            // Prefer a nearby point in the requested direction over one almost sideways.
            next=candidates.Where(n=>n.Kind!="circle"&&!Selected.Contains(n.Id)).Select(n=>
            {
                var delta=ObjectCenter(n)-anchor;var forward=Vector.Multiply(delta,direction);var side=Math.Abs(Vector.CrossProduct(delta,direction));
                return(Node:n,Forward:forward,Score:forward>.001?forward+2*side+side*side/forward:double.PositiveInfinity);
            }).Where(n=>n.Forward>.001).OrderBy(n=>n.Score).ThenBy(n=>n.Node.Id,StringComparer.Ordinal).Select(n=>n.Node).FirstOrDefault();
        }
        else
        {
            var center=ToWorld(new Point(UsableWidth/2,ActualHeight/2));var viewport=new Rect(0,0,UsableWidth,Math.Max(0,ActualHeight));
            next=candidates.Where(n=>n.Kind!="circle").OrderBy(n=>viewport.Contains(ToScreen(ObjectCenter(n)))?0:1).ThenBy(n=>(ObjectCenter(n)-center).LengthSquared).ThenBy(n=>n.Id,StringComparer.Ordinal).FirstOrDefault();
        }
        if(next==null)return false;
        navigationFrame=frame?.Id;navigationLastNode=next.Id;
        ClearAllSelection();Selected=[next.Id];AnnounceSelection();EnsureVisible(next.Id);return true;
    }
    double NodeRadius(Proposition node)=>GraphStyle.DisplayRadius(GraphStyle.Radius(node),Zoom);
    internal Rect ObjectBounds(Proposition node)=>node.Kind=="circle"?GraphGroups.Bounds(document,node,Zoom):GraphGroups.PointBounds(node,Zoom);
    Point ObjectCenter(Proposition node){if(node.Kind!="circle")return GraphStyle.Center(node);var b=ObjectBounds(node);return new Point(b.X+b.Width/2,b.Y+b.Height/2);}
    double OuterRadius(Proposition node)=>node.Kind=="circle"?ObjectBounds(node).Width/2:NodeRadius(node);
    internal Proposition? HitNode(Point world)
    {
        Proposition? direct=null,nearby=null;double directDistance=double.MaxValue,nearbyDistance=double.MaxValue;
        foreach(var n in visible.Where(n=>n.Kind!="circle")){var distance=(GraphStyle.Center(n)-world).Length*Zoom;var radius=OuterRadius(n)*Zoom;if(distance<=radius&&distance<directDistance){direct=n;directDistance=distance;}else if(distance<=Math.Max(radius+5,16)&&distance<nearbyDistance){nearby=n;nearbyDistance=distance;}}
        var caption=captions.FirstOrDefault(c=>!c.Placement.IsEdge&&c.Placement.Contains(ToScreen(world))).Placement;
        var ring=visible.Where(n=>n.Kind=="circle"&&Math.Abs((ObjectCenter(n)-world).Length-OuterRadius(n))*Zoom<=7).OrderBy(OuterRadius).FirstOrDefault();
        return direct??nearby??(caption!=null?index.GetValueOrDefault(caption.Id):null)??ring;
    }
    Point Port(Proposition node)=>ObjectCenter(node)+new Vector(OuterRadius(node)+13/Zoom,0);
    internal Proposition? ConnectionTarget(Point world,string? source)
    {
        var hit=HitNode(world);
        if(hit!=null&&hit.Id!=source)return hit;
        return visible.Where(n=>n.Id!=source&&n.Kind=="circle")
            .Where(n=>Math.Abs((ObjectCenter(n)-world).Length-OuterRadius(n))*Zoom<=14||(world-Port(n)).Length*Zoom<=11)
            .OrderBy(n=>Math.Abs((ObjectCenter(n)-world).Length-OuterRadius(n))).FirstOrDefault();
    }
    Point Anchor(Proposition node,Point toward){var center=ObjectCenter(node);var delta=toward-center;if(delta.Length<.001)return center;delta.Normalize();return center+delta*(OuterRadius(node)+4/Zoom);}
    static Point Curve(EdgePath edge,double t){var a=1-t;return new Point(a*a*edge.Start.X+2*a*t*edge.Control.X+t*t*edge.End.X,a*a*edge.Start.Y+2*a*t*edge.Control.Y+t*t*edge.End.Y);}
    (Point Tip,Vector Tangent,double T) ArrowAnchor(EdgePath edge,bool atEnd,double radius)
    {
        var parameter=new GraphCurve(edge.Start,edge.Control,edge.End).Anchor(atEnd,radius);var tangent=(edge.Control-edge.Start)*(1-parameter)+(edge.End-edge.Control)*parameter;return(Curve(edge,parameter),atEnd?tangent:-tangent,parameter);
    }
    void BuildGeometry()
    {
        if(!geometryDirty&&geometryZoom==Zoom)return;if(geometryDirty){captionReconsider=true;UpdateConnectionFilter();}geometryDirty=false;geometryZoom=Zoom;paths.Clear();captionsDirty=true;
        foreach(var item in projected)
        {
            if(allowedEdges!=null&&!allowedEdges.Contains(item.Edge.Id))continue;
            var a=EndpointCenter(item.From);var b=EndpointCenter(item.To);
            bool frameLink=document.Frame(item.From)!=null||document.Frame(item.To)!=null;
            if(frameLink){var ca=a;var cb=b;a=EndpointAnchor(item.From,cb);b=EndpointAnchor(item.To,ca);}
            var vector=b-a;if(vector.Length<.001)vector=new Vector(1,0);
            var normal=new Vector(-vector.Y,vector.X);normal.Normalize();if(string.CompareOrdinal(item.From,item.To)>0)normal=-normal;
            var rA=frameLink?0:OuterRadius(index[item.From])+2/Zoom;var rB=frameLink?0:OuterRadius(index[item.To])+2/Zoom;
            var curve=GraphCurve.Create(a,b,normal,item.Bend);var control=curve.Control;var start=curve.Start;var end=curve.End;
            var lo=!frameLink&&index[item.From].Kind=="circle"?curve.Anchor(false,rA):0;var hi=!frameLink&&index[item.To].Kind=="circle"?curve.Anchor(true,rB):1;if(lo>=hi)continue;
            var drawnStart=curve.At(lo);var drawnEnd=curve.At(hi);var drawnControl=drawnStart+((control-start)*(1-lo)+(end-control)*lo)*(hi-lo);
            var geometry=new StreamGeometry();using(var c=geometry.Open()){c.BeginFigure(drawnStart,false,false);c.QuadraticBezierTo(drawnControl,drawnEnd,true,false);}geometry.Freeze();var samples=new Point[17];for(int i=0;i<17;i++)samples[i]=curve.At(lo+(hi-lo)*i/16d);
            var bounds=new Rect(start,end);bounds.Union(control);paths.Add(new EdgePath(item.Edge,item.From,item.To,geometry,samples,start,control,end,bounds,curve.VisibleLength(rA,rB),rA,rB));
        }
    }
    double EdgeTipSize(EdgePath edge,string id)=>Math.Min(GraphStyle.ArrowSize(index.TryGetValue(id,out var node)?GraphStyle.Radius(node):18,Zoom),edge.VisibleLength/(edge.Edge.Direction=="both"?4.4:2.2));
    EdgePath? HitEdge(Point world)
    {
        BuildGeometry();var caption=captions.FirstOrDefault(c=>c.Placement.IsEdge&&c.Placement.Contains(ToScreen(world))).Placement;if(caption!=null)return paths.FirstOrDefault(p=>p.Edge.Id==caption.Id);
        EdgePath? found=null;double best=10/Zoom;foreach(var path in paths){var hitBounds=path.Bounds;hitBounds.Inflate(best,best);if(!hitBounds.Contains(world))continue;for(int i=1;i<path.Samples.Length;i++){var distance=GraphStyle.Distance(world,path.Samples[i-1],path.Samples[i]);if(distance<best){best=distance;found=path;}}}return found;
    }
    internal Dictionary<string,Point> RegionGrips()
    {
        var grips=new Dictionary<string,Point>();
        foreach(var r in regions)
        {
            var b=RegionBounds(r);if(b.IsEmpty)continue;
            // Use world coordinates so grips stay attached while zooming. Overflowing an
            // overcrowded border is worse than overlap; the overlap picker remains available.
            var origin=new Point(b.Left+Math.Min(b.Width/2,24),b.Top);var point=origin;
            var right=b.Right-Math.Min(8,b.Width/2);
            for(int step=0;step<=grips.Count;step++)
            {
                var candidate=origin+new Vector(step*26,0);if(candidate.X>right)break;
                if(grips.Values.All(p=>(p-candidate).Length>=23)){point=candidate;break;}
            }
            grips[r.Id]=point;
        }
        return grips;
    }
    internal Region? HitRegion(Point world)
    {
        var title=captions.FirstOrDefault(c=>!c.Placement.IsEdge&&document.Frame(c.Placement.Id)!=null&&c.Placement.Contains(ToScreen(world))).Placement;if(title!=null)return document.Frame(title.Id);
        var grip=RegionGrips().Where(pair=>(pair.Value-world).Length<11).OrderBy(pair=>(pair.Value-world).Length).FirstOrDefault();if(grip.Key!=null)return regions.First(r=>r.Id==grip.Key);
        double Distance(Region r){var b=RegionBounds(r);return new[]{GraphStyle.Distance(world,b.TopLeft,b.TopRight),GraphStyle.Distance(world,b.TopRight,b.BottomRight),GraphStyle.Distance(world,b.BottomRight,b.BottomLeft),GraphStyle.Distance(world,b.BottomLeft,b.TopLeft)}.Min();}
        return regions.Select(r=>(Region:r,Distance:Distance(r))).Where(pair=>pair.Distance<9/Zoom).OrderBy(pair=>pair.Distance).ThenBy(pair=>pair.Region.Id==SelectedRegion?0:1).Select(pair=>pair.Region).FirstOrDefault();
    }
    SolidColorBrush RegionColor(string color)=>GraphStyle.Brush(GraphStyle.NodeColor(color,Dark));
    static SolidColorBrush Tint(SolidColorBrush brush,byte opacity){var color=brush.Color;color.A=opacity;var result=new SolidColorBrush(color);result.Freeze();return result;}
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(background,null,new Rect(0,0,ActualWidth,ActualHeight));BuildGeometry();dc.PushTransform(new TranslateTransform(Offset.X,Offset.Y));dc.PushTransform(new ScaleTransform(Zoom,Zoom));
        var viewport=new Rect(ToWorld(new Point(-60,-60)),ToWorld(new Point(ActualWidth+60,ActualHeight+60)));
        foreach(var region in regions){var b=RegionBounds(region);if(!b.IsEmpty&&viewport.IntersectsWith(b))dc.DrawRectangle(Tint(RegionColor(GraphRegionColors.Base(region)),Dark?(byte)10:(byte)7),null,b);}
        if(regionGeometryDirty){intersectionPatches=GraphRegionColors.Intersections(regions);regionGeometryDirty=false;}
        foreach(var patch in intersectionPatches)if(viewport.IntersectsWith(patch.Bounds))dc.DrawRectangle(Tint(RegionColor(patch.Color),Dark?(byte)22:(byte)15),null,patch.Bounds);
        // Keep the active board's movement boundary visible without adding an interactive outer frame.
        if(BoardBounds is Rect board&&viewport.IntersectsWith(board))
            dc.DrawRoundedRectangle(null,new Pen(Tint((SolidColorBrush)regionBrush,Dark?(byte)130:(byte)155),1/Zoom),board,8/Zoom,8/Zoom);
        var grips=RegionGrips();foreach(var region in regions){var b=RegionBounds(region);if(b.IsEmpty||!viewport.IntersectsWith(b))continue;var stroke=linkTarget==region.Id?accent:RegionColor(GraphRegionColors.Base(region));dc.DrawRoundedRectangle(null,new Pen(stroke,(SelectedRegions.Contains(region.Id)?2.6:1.7)/Zoom),b,8/Zoom,8/Zoom);var grip=grips[region.Id];var gripWidth=Math.Min(16,b.Width);dc.DrawRoundedRectangle(stroke,new Pen(background,1),new Rect(grip.X-gripWidth/2,grip.Y-3,gripWidth,6),3,3);if(!GroupSelection&&SelectedRegion==region.Id){var handle=8/Zoom;dc.DrawRoundedRectangle(background,new Pen(stroke,1.7/Zoom),new Rect(b.Right-handle/2,b.Bottom-handle/2,handle,handle),1,1);}}
        foreach(var region in regions.Where(r=>r.Id==SelectedRegion||r.Id==hoverRegion||r.Id==linkTarget))if(!GroupSelection&&!lasso){var b=RegionBounds(region);var port=RegionPort(region);dc.DrawLine(new Pen(accent,1.8/Zoom),new Point(b.Right,port.Y),port);dc.DrawEllipse(background,new Pen(accent,2/Zoom),port,5/Zoom,5/Zoom);}
        var normalPen=new Pen(edgeBrush,GraphStyle.EdgeWidth(Zoom)){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};normalPen.Freeze();var activePen=new Pen(accent,GraphStyle.EdgeWidth(Zoom,true)){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};activePen.Freeze();
        foreach(var group in visible.Where(n=>n.Kind=="circle").OrderByDescending(n=>OuterRadius(n)))
        {
            var box=ObjectBounds(group);if(!viewport.IntersectsWith(box))continue;var center=ObjectCenter(group);var radius=OuterRadius(group);var active=Selected.Contains(group.Id)||hoverId==group.Id||linkTarget==group.Id;
            var stroke=group.MarkColor!=null?GraphStyle.Brush(GraphMarkColors.Display(group.MarkColor,Dark)):active?accent:GraphStyle.Brush(GraphStyle.NodeColor(group.Color,Dark));
            dc.DrawEllipse(null,new Pen(stroke,(active?3:group.MarkColor!=null?2.5:1.6)/Zoom),center,radius,radius);
            var grip=center-new Vector(0,radius);dc.DrawRoundedRectangle(stroke,new Pen(background,1),new Rect(grip.X-7,grip.Y-2.5,14,5),2.5,2.5);
            if(active&&!lasso&&!GroupSelection&&Selected.Count<=1){var port=Port(group);dc.DrawLine(new Pen(accent,1.8/Zoom),center+new Vector(radius,0),port);dc.DrawEllipse(background,new Pen(accent,2/Zoom),port,5/Zoom,5/Zoom);}
        }
        foreach(var edge in paths)
        {
            if(!viewport.IntersectsWith(edge.Bounds))continue;
            bool active=SelectedEdge==edge.Edge.Id||hoverEdge==edge.Edge.Id;
            var brush=edge.Edge.MarkColor is string mark?GraphStyle.Brush(GraphMarkColors.Display(mark,Dark)):active?accent:edgeBrush;
            var pen=edge.Edge.MarkColor!=null?new Pen(brush,GraphStyle.EdgeWidth(Zoom,active)+.7/Zoom){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round}:active?activePen:normalPen;
            if(GraphFullText.IsReference(edge.Edge)){pen=pen.Clone();pen.DashStyle=DashStyles.Dash;}
            dc.DrawGeometry(null,pen,edge.Geometry);
            var start=ArrowAnchor(edge,false,edge.StartRadius);var end=ArrowAnchor(edge,true,edge.EndRadius);
            var startSize=EdgeTipSize(edge,edge.From);var endSize=EdgeTipSize(edge,edge.To);
            if(start.T>=end.T)continue;
            if(edge.Edge.Direction!="reverse"&&endSize*Zoom>=2)GraphStyle.DrawTip(dc,edge.Edge.Label,end.Tip,end.Tangent,brush,endSize,Math.Min(1.9/Zoom,endSize*.3),background);
            if(edge.Edge.Direction!="forward"&&startSize*Zoom>=2)GraphStyle.DrawTip(dc,edge.Edge.Label,start.Tip,start.Tangent,brush,startSize,Math.Min(1.9/Zoom,startSize*.3),background);
        }
        foreach(var node in visible.Where(n=>n.Kind!="circle").OrderBy(n=>Selected.Contains(n.Id)?1:0))
        {
            var center=GraphStyle.Center(node);var radius=NodeRadius(node);var outer=OuterRadius(node);if(!viewport.IntersectsWith(new Rect(center.X-outer,center.Y-outer,outer*2,outer*2)))continue;var selected=Selected.Contains(node.Id);var hovered=node.Id==hoverId||node.Id==linkTarget;var key=node.MarkColor is string mark?"mark:"+mark:GraphRegionColors.ForNode(node,document.Regions,document);
            if(!nodeBrushes.TryGetValue(key,out var fill)){fill=GraphStyle.Brush(GraphMarkColors.Node(document,node,Dark));nodeBrushes[key]=fill;}
            dc.DrawEllipse(background,null,center,outer+2/Zoom,outer+2/Zoom);
            dc.DrawEllipse(fill,null,center,radius,radius);
            if(node.Kind=="circle")dc.DrawEllipse(null,new Pen(fill,1.7/Zoom),center,outer,outer);
            if(selected||hovered){var ring=outer+(selected?4:2.5)/Zoom;dc.DrawEllipse(null,new Pen(accent,(selected?2.2:1.3)/Zoom),center,ring,ring);}
            if((selected||hovered)&&!lasso&&!GroupSelection&&Selected.Count<=1){var port=Port(node);dc.DrawLine(new Pen(accent,1.8/Zoom),new Point(center.X+outer,center.Y),port);dc.DrawEllipse(background,new Pen(accent,2/Zoom),port,5/Zoom,5/Zoom);}
        }
        if((linkDragging||LinkMode)&&LinkStart!=null&&document.HasEndpoint(LinkStart)){var end=linkTarget!=null?EndpointAnchor(linkTarget,EndpointCenter(LinkStart)):linkEnd;var start=EndpointAnchor(LinkStart,linkTarget!=null?EndpointCenter(linkTarget):end);dc.DrawLine(new Pen(accent,1.8/Zoom){DashStyle=DashStyles.Dash},start,end);dc.DrawEllipse(accent,null,end,4/Zoom,4/Zoom);}
        if(GroupSelection&&!lasso){var envelope=SelectionEnvelope();if(!envelope.IsEmpty)dc.DrawRectangle(null,new Pen(accent,1/Zoom){DashStyle=DashStyles.Dash},envelope);}
        if(selectionRect is Rect rect){dc.PushOpacity(.1);dc.DrawRectangle(accent,null,rect);dc.Pop();dc.DrawRectangle(null,new Pen(accent,1/Zoom),rect);}
        dc.Pop();dc.Pop();
        LayoutCaptions();foreach(var (placement,text) in captions){dc.PushTransform(new MatrixTransform(placement.Transform));dc.DrawText(text,placement.TextBounds.TopLeft);dc.Pop();}
    }
    internal IReadOnlyList<GraphCaptions.Placement> LayoutCaptions()
    {
        BuildGeometry();if(!ShowCaptions){captions.Clear();return [];}
        var viewport=new Rect(0,0,Math.Max(0,ActualWidth),Math.Max(0,ActualHeight));var dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;var textScale=GraphCaptions.TextScale(Zoom);
        var reconsider=captionReconsider&&!IsInteracting;
        var view=(Zoom,Offset,viewport.Size,dpi);if(!captionsDirty&&!reconsider&&captionView==view)return captions.Select(c=>c.Placement).ToArray();
        if(reconsider)captionReconsider=false;
        captionView=view;captionsDirty=false;captions.Clear();
        var layout=new CaptionLayout(viewport);
        foreach(var node in visible.Where(n=>n.Kind!="circle"))layout.AddCircle(ToScreen(ObjectCenter(node)),OuterRadius(node)*Zoom+3);
        foreach(var edge in paths)
        {
            for(int i=1;i<edge.Samples.Length;i++)layout.AddLine(ToScreen(edge.Samples[i-1]),ToScreen(edge.Samples[i]));
            void Head(bool atEnd)
            {
                var id=atEnd?edge.To:edge.From;var head=ArrowAnchor(edge,atEnd,atEnd?edge.EndRadius:edge.StartRadius);var direction=head.Tangent;if(direction.Length<.001)return;direction.Normalize();
                var tip=ToScreen(head.Tip);var size=EdgeTipSize(edge,id)*Zoom;if(size<2)return;
                layout.AddLine(tip,tip-direction*size*1.85,size*.8+2,850);
            }
            if(edge.Edge.Direction!="reverse")Head(true);if(edge.Edge.Direction!="forward")Head(false);
        }
        FormattedText Text(string id,string caption)
        {
            if(captionText.TryGetValue(id,out var entry)&&entry.Text==caption&&entry.Dpi==dpi)return entry.Layout;
            var layout=GraphCaptions.Measure(caption,captionBrush,dpi);captionText[id]=(caption,dpi,layout);return layout;
        }
        void Place(string id,IEnumerable<GraphCaptions.Candidate> candidates,FormattedText text)
        {
            var choice=layout.Choose(candidates,captionSlots.TryGetValue(id,out var slot)?slot:null,reconsider);var placement=choice.Placement;captionSlots[id]=choice.Slot;
            if(viewport.IntersectsWith(placement.Bounds)){captions.Add((placement,text));layout.AddTitle(placement);}
        }
        foreach(var frame in regions)
        {
            if(string.IsNullOrWhiteSpace(frame.Caption))continue;
            var text=Text(frame.Id,frame.Caption);
            var placement=GraphCaptions.Frame(frame.Id,new Size(text.WidthIncludingTrailingWhitespace,text.Height),ToScreen(RegionBounds(frame).TopLeft),textScale);
            if(viewport.IntersectsWith(placement.Bounds)){captions.Add((placement,text));layout.AddTitle(placement);}
        }
        foreach(var node in visible)
        {
            if(string.IsNullOrWhiteSpace(node.Caption))continue;var center=ToScreen(ObjectCenter(node));
            var text=Text(node.Id,node.Caption);Place(node.Id,GraphCaptions.NodeCandidates(node.Id,new Size(text.WidthIncludingTrailingWhitespace,text.Height),center,OuterRadius(node)*Zoom/textScale,textScale),text);
        }
        foreach(var edge in paths)
        {
            if(string.IsNullOrWhiteSpace(edge.Edge.Caption))continue;
            var anchors=new[]{.5,.35,.65,.2,.8}.Select(t=>(ToScreen(Curve(edge,t)),(edge.Control-edge.Start)*(1-t)+(edge.End-edge.Control)*t)).ToArray();
            var text=Text(edge.Edge.Id,edge.Edge.Caption);Place(edge.Edge.Id,GraphCaptions.EdgeCandidates(edge.Edge.Id,new Size(text.WidthIncludingTrailingWhitespace,text.Height),anchors,textScale),text);
        }
        return captions.Select(c=>c.Placement).ToArray();
    }
    void ClearOthers(){SelectedEdge=SelectedRegion=null;}
    public Rect SelectedBounds()
    {
        var bounds=Rect.Empty;foreach(var n in visible.Where(n=>Selected.Contains(n.Id)))bounds.Union(ObjectBounds(n));foreach(var r in regions.Where(r=>SelectedRegions.Contains(r.Id)))bounds.Union(RegionBounds(r));return bounds;
    }
    Rect SelectionEnvelope(){var bounds=SelectedBounds();if(SelectionBox is Rect box)bounds.Union(box);if(!bounds.IsEmpty&&SelectionBox==null)bounds.Inflate(10/Zoom,10/Zoom);return bounds;}
    bool HitSelection(Point world)=>GroupSelection&&!SelectionEnvelope().IsEmpty&&SelectionEnvelope().Contains(world);
    public void ClearAllSelection(){Selected.Clear();SelectedRegions.Clear();SelectedEdge=null;SelectionBox=null;}
    internal void BeginSelectionMove()
    {
        selectionMove=new SelectionMove(document,Selected,SelectedRegions);boxBeforeMove=SelectionBox;dragBounds=selectionMove.Bounds.IsEmpty?SelectionEnvelope():selectionMove.Bounds;PrepareDragSpacing(selectionMove.NodeIds);dragging=true;Cursor=Cursors.SizeAll;
    }
    internal void MoveSelection(Vector delta)
    {
        if(selectionMove==null)return;delta=GraphSpacing.LimitDrag(delta,previousDrag,dragLimits,dragBounds,BoardBounds);previousDrag=delta;selectionMove.Apply(delta);
        if(boxBeforeMove is Rect box){box.Offset(delta);SelectionBox=box;}geometryDirty=regionGeometryDirty=true;InvalidateVisual();
    }
    internal void EndSelectionMove(){selectionMove=null;dragging=false;}
    void PrepareDragSpacing(IEnumerable<string> ids)
    {
        var moving=ids.ToHashSet();previousDrag=new Vector();dragLimits=projected.Where(e=>index.ContainsKey(e.From)&&index.ContainsKey(e.To)&&(moving.Contains(e.From)!=moving.Contains(e.To))).Select(e=>{var a=index[moving.Contains(e.From)?e.From:e.To];var b=index[moving.Contains(e.From)?e.To:e.From];return new GraphSpacing.DragLimit(ObjectCenter(a),ObjectCenter(b),GraphStyle.MinimumNodeDistance(GraphStyle.Radius(a)+(a.Kind=="circle"?7:0),GraphStyle.Radius(b)+(b.Kind=="circle"?7:0)));}).Distinct().ToArray();
    }
    internal void BeginBoxSelection(Point start,bool additive)
    {
        selectionBeforeBox=Selected.ToHashSet();regionsBeforeBox=SelectedRegions.ToHashSet();edgeBeforeBox=SelectedEdge;previousSelectionBox=SelectionBox;boxAdditive=additive;lassoBase=additive?Selected.ToHashSet():[];regionLassoBase=additive?SelectedRegions.ToHashSet():[];boxStart=start;lasso=true;selectionRect=new Rect(start,start);Cursor=Cursors.Cross;
    }
    internal void UpdateBoxSelection(Point end)
    {
        if(!lasso)return;selectionRect=new Rect(boxStart,end);SelectedEdge=null;Selected=visible.Where(n=>{var p=GraphStyle.Center(n);var r=OuterRadius(n);return n.Kind=="circle"?selectionRect.Value.Contains(ObjectBounds(n)):selectionRect.Value.IntersectsWith(new Rect(p.X-r,p.Y-r,r*2,r*2));}).Select(n=>n.Id).Concat(lassoBase).ToHashSet();SelectedRegions=regions.Where(r=>selectionRect.Value.Contains(RegionBounds(r))).Select(r=>r.Id).Concat(regionLassoBase).ToHashSet();InvalidateVisual();
    }
    internal Rect? FinishBoxSelection(bool apply=true)
    {
        if(!lasso)return null;var box=selectionRect;lasso=false;selectionRect=null;if(apply){if(boxAdditive&&box is Rect combined){combined.Union(SelectedBounds());if(previousSelectionBox is Rect previous)combined.Union(previous);box=combined;}SelectionBox=box;Cursor=Cursors.SizeAll;AnnounceSelection();return box;}return null;
    }
    public bool CancelBoxSelection()
    {
        if(!lasso&&!drawingBox)return false;if(lasso){Selected=selectionBeforeBox;SelectedRegions=regionsBeforeBox;SelectedEdge=edgeBeforeBox;SelectionBox=previousSelectionBox;}lasso=drawingBox=rightGesture=false;selectionRect=null;DrawingRegion=false;ReleaseMouseCapture();Cursor=GroupSelection?Cursors.SizeAll:Cursors.Arrow;AnnounceSelection();return true;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if(IsPreview){e.Handled=true;return;}
        base.OnMouseDown(e);CancelLayout();StopAnimation();Focus();down=last=e.GetPosition(this);var world=ToWorld(down);moved=false;selectionMove=null;
        if(e.ChangedButton==MouseButton.Middle){panning=true;CaptureMouse();Cursor=Cursors.ScrollAll;e.Handled=true;return;}
        if(e.ChangedButton==MouseButton.Right)
        {
            rightGesture=true;var node=HitNode(world);var region=HitRegion(world);
            var modifying=(Keyboard.Modifiers&(ModifierKeys.Control|ModifierKeys.Shift))!=0;
            var handle=!GroupSelection&&SelectedRegion!=null?regions.FirstOrDefault(r=>r.Id==SelectedRegion&&(world-RegionBounds(r).BottomRight).Length<12/Zoom):null;
            if(DrawingRegion){drawingBox=true;selectionRect=new Rect(world,world);Cursor=Cursors.Cross;}
            else if(GroupSelection&&!modifying&&(node!=null&&Selected.Contains(node.Id)||region!=null&&SelectedRegions.Contains(region.Id)||node==null&&region==null&&HitSelection(world)))BeginSelectionMove();
            else if(modifying)BeginBoxSelection(world,true);
            else if(node?.Kind=="circle"){Selected=[node.Id];ClearOthers();AnnounceSelection();BeginSelectionMove();}
            else if(handle!=null||region!=null&&node==null)
            {
                movingRegion=handle??region!;originalRegion=RegionBounds(movingRegion);Selected.Clear();SelectedEdge=null;SelectedRegion=movingRegion.Id;AnnounceSelection();resizingRegion=handle!=null;dragging=true;movingContents=MoveRegionContents||Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);regionMove=new RegionMove(document,movingRegion,movingContents);PrepareDragSpacing(movingContents?GraphBoard.Members(document,movingRegion).Select(n=>n.Id):[]);Cursor=resizingRegion?Cursors.SizeNWSE:Cursors.SizeAll;
            }
            else BeginBoxSelection(world,(Keyboard.Modifiers&(ModifierKeys.Control|ModifierKeys.Shift))!=0);
            CaptureMouse();e.Handled=true;return;
        }
        if(e.ChangedButton!=MouseButton.Left)return;
        if(DrawingRegion){drawingBox=true;selectionRect=new Rect(world,world);CaptureMouse();e.Handled=true;return;}
        var hit=HitNode(world);var frame=HitRegion(world);var multi=(Keyboard.Modifiers&(ModifierKeys.Shift|ModifierKeys.Control))!=0;
        var linkHit=LinkMode?ConnectionEndpointTarget(world,LinkStart):null;
        if(LinkMode&&linkHit!=null){if(LinkStart==null){LinkStart=linkHit;SelectEndpoint(linkHit);AnnounceSelection();}else if(LinkStart!=linkHit){var from=LinkStart;LinkStart=null;Connect?.Invoke(from,linkHit);}InvalidateVisual();e.Handled=true;return;}
        var framePort=!GroupSelection?regions.FirstOrDefault(r=>(r.Id==SelectedRegion||r.Id==hoverRegion)&&(world-RegionPort(r)).Length<11/Zoom):null;
        if(framePort!=null){SelectEndpoint(framePort.Id);LinkStart=framePort.Id;linkEnd=world;linkDragging=true;CaptureMouse();AnnounceSelection();e.Handled=true;return;}
        if(GroupSelection&&!multi&&e.ClickCount==1&&(hit!=null&&Selected.Contains(hit.Id)||frame!=null&&SelectedRegions.Contains(frame.Id)||hit==null&&frame==null&&HitSelection(world))){BeginSelectionMove();CaptureMouse();e.Handled=true;return;}
        var port=!GroupSelection&&Selected.Count<=1?visible.LastOrDefault(n=>(Selected.Contains(n.Id)||hoverId==n.Id)&&(world-Port(n)).Length<11/Zoom):null;
        if(port!=null&&(hit==null||hit.Id==port.Id&&(ObjectCenter(hit)-world).Length>OuterRadius(hit)+2/Zoom)){Selected=[port.Id];ClearOthers();LinkStart=port.Id;linkEnd=world;linkDragging=true;CaptureMouse();AnnounceSelection();e.Handled=true;return;}
        if(hit!=null)
        {
            if(multi){if(!Selected.Add(hit.Id))Selected.Remove(hit.Id);SelectionBox=null;}else{Selected=[hit.Id];SelectedRegions.Clear();}SelectedEdge=null;AnnounceSelection();
            if(e.ClickCount==2){if(hit.Kind=="circle")EnterCircle?.Invoke(hit.Id);else EditRequested?.Invoke();e.Handled=true;return;}
            if(Selected.Contains(hit.Id)){BeginSelectionMove();CaptureMouse();}e.Handled=true;return;
        }
        if(e.ClickCount==2){frame??=regions.Where(r=>RegionBounds(r).Contains(world)).OrderBy(r=>r.Width*r.Height).FirstOrDefault();if(frame!=null){EnterRegion?.Invoke(frame.Id);e.Handled=true;return;}}
        if(frame!=null){if(multi){if(!SelectedRegions.Add(frame.Id))SelectedRegions.Remove(frame.Id);SelectionBox=null;}else{Selected.Clear();SelectedRegion=frame.Id;}SelectedEdge=null;AnnounceSelection();e.Handled=true;return;}
        if(HitEdge(world) is EdgePath edge){Selected.Clear();SelectedRegion=null;SelectedEdge=edge.Edge.Id;AnnounceSelection();e.Handled=true;return;}
        if(e.ClickCount==2){CreateNode?.Invoke(world);e.Handled=true;return;}
        if((Keyboard.Modifiers&ModifierKeys.Shift)!=0)BeginBoxSelection(world,(Keyboard.Modifiers&ModifierKeys.Control)!=0);else panning=true;
        CaptureMouse();e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(IsPreview)return;
        base.OnMouseMove(e);var current=e.GetPosition(this);var world=ToWorld(current);
        if(drawingBox){if((current-down).Length>3)moved=true;var rect=new Rect(ToWorld(down),world);if(BoardBounds is Rect bounds)rect.Intersect(bounds);selectionRect=rect;InvalidateVisual();}
        else if(linkDragging||LinkMode){linkEnd=world;linkTarget=ConnectionEndpointTarget(world,LinkStart);InvalidateVisual();}
        else if(dragging)
        {
            if(!moved&&(current-down).Length>3){if(movingRegion!=null||selectionMove?.Count>0)BeforeChange?.Invoke();moved=true;}
            if(moved)
            {
                var delta=(current-down)/Zoom;
                if(movingRegion!=null)
                {
                    movingRegion.IsAbsolute=true;
                    if(resizingRegion){movingRegion.Width=Math.Max(60,originalRegion.Width+delta.X);movingRegion.Height=Math.Max(60,originalRegion.Height+delta.Y);if(BoardBounds is Rect parent){movingRegion.Width=Math.Min(movingRegion.Width,parent.Right-movingRegion.X);movingRegion.Height=Math.Min(movingRegion.Height,parent.Bottom-movingRegion.Y);}}
                    else{if(movingContents)delta=GraphSpacing.LimitDrag(delta,previousDrag,dragLimits,originalRegion,BoardBounds);previousDrag=delta;regionMove?.Apply(delta,BoardBounds);}
                }
                else
                {
                    MoveSelection(delta);
                }
                geometryDirty=true;if(movingRegion!=null)regionGeometryDirty=true;InvalidateVisual();
            }
        }
        else if(panning){if((current-down).Length>3){moved=true;Cursor=Cursors.ScrollAll;}Offset+=current-last;targetOffset=Offset;InvalidateVisual();ViewChanged?.Invoke();}
        else if(lasso){if((current-down).Length>3)moved=true;if(moved)UpdateBoxSelection(world);}
        else if(rightGesture){if((current-down).Length>3)moved=true;}
        else
        {
            var hover=HitNode(world)?.Id;if(hover==null&&!GroupSelection)hover=visible.LastOrDefault(n=>(Selected.Contains(n.Id)||hoverId==n.Id)&&(world-Port(n)).Length<13/Zoom)?.Id;var edge=hover==null?HitEdge(world)?.Edge.Id:null;
            var frameHover=HitRegion(world)?.Id??regions.FirstOrDefault(r=>(r.Id==SelectedRegion||r.Id==hoverRegion)&&(world-RegionPort(r)).Length<13/Zoom)?.Id;
            if(frameHover!=hoverRegion){hoverRegion=frameHover;InvalidateVisual();}
            if(hover!=hoverId||edge!=hoverEdge){hoverId=hover;hoverEdge=edge;InvalidateVisual();}Cursor=DrawingRegion||LinkMode?Cursors.Cross:HitSelection(world)?Cursors.SizeAll:hover!=null||edge!=null||HitRegion(world)!=null?Cursors.Hand:Cursors.Arrow;
        }
        last=current;
    }
    protected override void OnMouseLeave(MouseEventArgs e){base.OnMouseLeave(e);if(!IsMouseCaptured){hoverId=hoverEdge=null;InvalidateVisual();}}
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if(IsPreview){e.Handled=true;return;}
        base.OnMouseUp(e);bool changed=dragging&&moved&&(movingRegion!=null||selectionMove?.Count>0);var context=rightGesture&&!moved;
        var boxCompleted=rightGesture&&lasso&&moved;var selectedBox=lasso?FinishBoxSelection(moved):null;
        Rect? created=drawingBox&&selectionRect is Rect draft&&!draft.IsEmpty&&draft.Width*Zoom>12&&draft.Height*Zoom>12?new Rect(draft.X,draft.Y,Math.Max(60,draft.Width),Math.Max(60,draft.Height)):null;
        if(drawingBox){DrawingRegion=false;drawingBox=false;}
        if(linkDragging){var from=LinkStart;var to=ConnectionEndpointTarget(ToWorld(e.GetPosition(this)),from);LinkStart=linkTarget=null;linkDragging=false;if(from!=null&&to!=null&&from!=to)Connect?.Invoke(from,to);}
        if(context)
        {
            var world=ToWorld(e.GetPosition(this));var node=HitNode(world);var edge=HitEdge(world);var region=HitRegion(world);
            if(selectionMove!=null&&GroupSelection){}else if(region!=null&&movingRegion!=null){Selected.Clear();SelectedEdge=null;SelectedRegion=movingRegion.Id;}else if(node!=null){if(!Selected.Contains(node.Id)){Selected=[node.Id];SelectedRegions.Clear();}SelectedEdge=null;}else if(region!=null){if(!SelectedRegions.Contains(region.Id)){Selected.Clear();SelectedRegion=region.Id;}SelectedEdge=null;}else if(edge!=null&&!GroupSelection){ClearAllSelection();SelectedEdge=edge.Edge.Id;}else if(!GroupSelection)ClearAllSelection();AnnounceSelection();
        }
        else if(panning&&!moved&&e.ChangedButton==MouseButton.Left){ClearAllSelection();LinkStart=null;AnnounceSelection();}
        dragging=panning=lasso=rightGesture=resizingRegion=false;movingRegion=null;regionMove=null;EndSelectionMove();selectionRect=null;ReleaseMouseCapture();Cursor=GroupSelection?Cursors.SizeAll:Cursors.Arrow;if(changed)Changed?.Invoke();if(created is Rect box)CreateRegion?.Invoke(box);if(boxCompleted&&selectedBox is Rect selection)BoxSelectionCompleted?.Invoke(selection);else if(context)ContextRequested?.Invoke();InvalidateVisual();
    }
    protected override void OnLostMouseCapture(MouseEventArgs e){base.OnLostMouseCapture(e);var changed=dragging&&moved&&(movingRegion!=null||selectionMove?.Count>0);dragging=panning=lasso=linkDragging=rightGesture=drawingBox=resizingRegion=false;movingRegion=null;regionMove=null;selectionMove=null;selectionRect=null;LinkStart=linkTarget=null;if(changed)Changed?.Invoke();InvalidateVisual();}
    protected override void OnMouseWheel(MouseWheelEventArgs e){if(!IsPreview)ZoomBy(Math.Exp(e.Delta*.00135),e.GetPosition(this));e.Handled=true;}
}
