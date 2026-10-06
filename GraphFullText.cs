using System.Text;
using System.Windows;

namespace Papergraph;

public sealed record FullTextIssue(string Code,string Message,string? ObjectId);
public sealed record FullTextPiece(string NodeId,string Text,string Separator,string? FrameId,string? CircleId);
public sealed record FullTextPage(string Id,string Text,IReadOnlyList<FullTextPiece> Pieces,IReadOnlyList<FullTextIssue> Issues,
    IReadOnlyList<string> ObjectIds,IReadOnlyList<string> EdgeIds,int BlockedPoints);
public sealed record FullTextResult(string Text,IReadOnlyList<FullTextPiece> Pieces,IReadOnlyList<FullTextIssue> Issues,
    int ExcludedPoints,int ReferenceEdges,int BlockedPoints)
{
    public IReadOnlyList<FullTextPage> Pages {get;init;}=[];
    public IReadOnlyList<FullTextIssue> GeneralIssues {get;init;}=[];
}

// A read-only projection of the graph. Relation meaning and manuscript participation are independent.
public static class GraphFullText
{
    public static bool IsReference(Relation edge)=>edge.TextRole=="reference";
    sealed record Item(string Id,string Kind,string Name,string Body,string Parent,double X,double Y);
    sealed record Link(string From,string To,Relation Edge);

    public static FullTextResult Build(GraphDocument doc)
    {
        var items=new Dictionary<string,Item>();
        var overlaps=new Dictionary<string,string[]>();
        string Name(string caption,string body,string fallback)
        {
            var text=string.IsNullOrWhiteSpace(caption)?body:caption;
            text=text.Split('\n')[0].Trim();return text.Length==0?fallback:text.Length>36?text[..36]+"…":text;
        }
        Region? Owner(string? parent,Point center,string id)
        {
            var candidates=doc.Regions.Where(r=>r.Parent==parent&&GraphBoard.Bounds(r).Contains(center))
                .OrderBy(r=>r.Width*r.Height).ThenBy(r=>r.Id,StringComparer.Ordinal).ToArray();
            if(candidates.Length>1&&candidates.Skip(1).Any(r=>!GraphBoard.Bounds(r).Contains(GraphBoard.Bounds(candidates[0]))))
                overlaps[id]=candidates.Select(r=>r.Id).ToArray();
            return candidates.FirstOrDefault();
        }
        foreach(var f in doc.Regions)
        {
            var outer=doc.Regions.Where(r=>r.Id!=f.Id&&r.Parent==f.Parent&&r.Width*r.Height>f.Width*f.Height+.01&&GraphBoard.Bounds(r).Contains(GraphBoard.Bounds(f)))
                .OrderBy(r=>r.Width*r.Height).ThenBy(r=>r.Id,StringComparer.Ordinal).FirstOrDefault();
            items[f.Id]=new(f.Id,"frame",Name(f.Caption,f.Title,"□"),f.Title,outer?.Id??f.Parent??"",f.X,f.Y);
        }
        foreach(var n in doc.Nodes)
        {
            var center=GraphGroups.Center(doc,n);
            items[n.Id]=new(n.Id,n.Kind,Name(n.Caption,n.Title,n.Kind=="circle"?"◎":"Empty point"),n.Title,
                Owner(n.Parent,center,n.Id)?.Id??n.Parent??"",center.X,center.Y);
        }
        var paths=new Dictionary<string,List<string>>();
        List<string> Path(string id)
        {
            if(paths.TryGetValue(id,out var cached))return cached;
            var path=new List<string>();var seen=new HashSet<string>();var cur=id;
            while(cur.Length>0&&items.TryGetValue(cur,out var item)&&seen.Add(cur)){path.Add(cur);cur=item.Parent;}
            path.Reverse();paths[id]=path;return path;
        }
        var bodyEdges=doc.Edges.Where(e=>!IsReference(e)&&e.Direction!="both"&&items.ContainsKey(e.From)&&items.ContainsKey(e.To)).ToArray();
        var endpoints=bodyEdges.SelectMany(e=>new[]{e.From,e.To}).ToHashSet();
        var components=endpoints.ToDictionary(id=>id,id=>id);
        string Component(string id)
        {
            var root=id;while(components[root]!=root)root=components[root];
            while(components[id]!=id){var next=components[id];components[id]=root;id=next;}return root;
        }
        void Join(string a,string b){var first=Component(a);var second=Component(b);if(first!=second)components[second]=first;}
        foreach(var edge in bodyEdges)Join(edge.From,edge.To);
        // A container can stand for one connected manuscript. It must not silently merge
        // multiple independent member graphs, even when its outline has external arrows.
        var containers=endpoints.Where(id=>items[id].Kind!="point").OrderByDescending(id=>Path(id).Count)
            .ToDictionary(id=>id,id=>endpoints.Where(child=>child!=id&&Path(child).Contains(id)).ToArray());
        bool merged;
        do
        {
            merged=false;
            foreach(var container in containers)
            {
                var inside=container.Value.Select(Component).Distinct().ToArray();
                if(inside.Length==1&&Component(container.Key)!=inside[0]){Join(container.Key,inside[0]);merged=true;}
            }
        }while(merged);
        // Relations are appended in creation order and that order survives save/load and Undo.
        // GroupBy preserves the first surviving Body connection of each component.
        var allPages=bodyEdges.GroupBy(e=>Component(e.From)).Select(g=>BuildPage(g.ToArray())).ToArray();
        var pages=allPages.Where(p=>p.ObjectIds.Any(id=>items[id].Kind=="point")).ToArray();
        var unresolvedDirections=doc.Edges.Where(e=>!IsReference(e)&&e.Direction=="both")
            .Select(e=>new FullTextIssue("bidirectional","This bidirectional arrow has no single reading direction and is excluded from text ordering and page connections.",e.Id));
        var pointComponents=endpoints.Where(id=>items[id].Kind=="point").Select(Component).ToHashSet();
        var containerIssues=containers.Where(c=>pointComponents.Contains(Component(c.Key))&&c.Value.Select(Component).Distinct().Skip(1).Any())
            .Select(c=>new FullTextIssue("container-pages","“"+items[c.Key].Name+"” contains separate Body structures. Its outline connection cannot select a single manuscript; connect the member structures to continue them together.",c.Key));
        var generalIssues=unresolvedDirections.Concat(containerIssues).Concat(allPages.Except(pages).SelectMany(p=>p.Issues)).ToArray();
        return new(string.Join("\f",pages.Select(p=>p.Text)),pages.SelectMany(p=>p.Pieces).ToArray(),
            generalIssues.Concat(pages.SelectMany(p=>p.Issues)).ToArray(),
            doc.Nodes.Count(n=>n.Kind=="point"&&!endpoints.Contains(n.Id)),doc.Edges.Count(IsReference),pages.Sum(p=>p.BlockedPoints)){Pages=pages,GeneralIssues=generalIssues};

        FullTextPage BuildPage(Relation[] pageEdges)
        {
        var issues=new List<FullTextIssue>();
        var active=new HashSet<string>();var connectedPoints=new HashSet<string>();var links=new Dictionary<string,List<Link>>();
        foreach(var edge in pageEdges)
        {
            var from=edge.Direction=="reverse"?edge.To:edge.From;
            var to=edge.Direction=="reverse"?edge.From:edge.To;
            if(!items.ContainsKey(from)||!items.ContainsKey(to))continue;
            foreach(var id in new[]{from,to})
            {
                active.UnionWith(Path(id));if(items[id].Kind=="point")connectedPoints.Add(id);
            }
            var a=Path(from);var b=Path(to);int shared=0;
            while(shared<Math.Min(a.Count,b.Count)&&a[shared]==b[shared])shared++;
            if(shared==a.Count||shared==b.Count)
            {
                // A member-to-wrapper arrow does not specify the order of the wrapper's other members.
                issues.Add(new("container-boundary","An arrow between a member and its circle or frame does not order the members. Check the Body arrows inside the group.",edge.Id));continue;
            }
            var scope=shared==0?"":a[shared-1];
            if(!links.TryGetValue(scope,out var local))links[scope]=local=[];
            local.Add(new(a[shared],b[shared],edge));
        }
        foreach(var pair in overlaps.Where(p=>active.Contains(p.Key)))
            issues.Add(new("overlap","“"+items[pair.Key].Name+"” lies in overlapping frames and is provisionally assigned to the smallest. Check its paragraph grouping.",pair.Key));
        var children=active.GroupBy(id=>items[id].Parent).ToDictionary(g=>g.Key,g=>g.ToList());
        var ordered=new Dictionary<string,List<string>>();var blocked=new HashSet<string>();
        foreach(var pair in children)
        {
            var scope=pair.Key;var ids=pair.Value;var set=ids.ToHashSet();
            var local=(links.GetValueOrDefault(scope)??[]).Where(e=>set.Contains(e.From)&&set.Contains(e.To)).ToArray();
            var outgoing=ids.ToDictionary(id=>id,_=>new Dictionary<string,List<Relation>>());
            var indegree=ids.ToDictionary(id=>id,_=>0);
            foreach(var link in local)
            {
                if(!outgoing[link.From].TryGetValue(link.To,out var relations))
                {outgoing[link.From][link.To]=relations=[];indegree[link.To]++;}
                relations.Add(link.Edge);
            }
            IOrderedEnumerable<string> Spatial(IEnumerable<string> source)=>source.OrderBy(id=>items[id].Y).ThenBy(id=>items[id].X).ThenBy(id=>id,StringComparer.Ordinal);
            int Rank(List<Relation> es)=>es.Where(e=>e.TextOrder>0).Select(e=>e.TextOrder).DefaultIfEmpty(int.MaxValue).Min();
            string ScopeName()=>scope.Length==0?"This page":"“"+items[scope].Name+"”";
            var roots=Spatial(ids.Where(id=>indegree[id]==0)).ToList();
            if(roots.Count>1)issues.Add(new("multiple-starts",ScopeName()+" has "+roots.Count+" reading starts. Fragments are provisionally ordered by position; connect them with Body arrows.",roots[0]));
            foreach(var entry in outgoing.Where(e=>e.Value.Count>1))
            {
                var ranks=entry.Value.Values.Select(Rank).ToArray();
                // A transitive shortcut is not an independently ordered branch.
                bool Reaches(string start,string target)
                {
                    var seen=new HashSet<string>();var pending=new Stack<string>();pending.Push(start);
                    while(pending.TryPop(out var id)){if(!seen.Add(id))continue;if(id==target)return true;foreach(var next in outgoing[id].Keys)pending.Push(next);}return false;
                }
                var targets=entry.Value.Keys.ToArray();bool ambiguous=false;
                for(int i=0;i<targets.Length&&!ambiguous;i++)for(int j=i+1;j<targets.Length;j++)
                    if(!Reaches(targets[i],targets[j])&&!Reaches(targets[j],targets[i])){ambiguous=true;break;}
                if(ambiguous&&(ranks.Contains(int.MaxValue)||ranks.Distinct().Count()!=ranks.Length))
                    issues.Add(new("branch","“"+items[entry.Key].Name+"” has unordered branches. Position is used provisionally; set a Branch order for each Body arrow.",entry.Value.Values.First()[0].Id));
            }
            // Depth-first priorities keep one branch together; Kahn's indegree check delays a shared join until all predecessors finish.
            var priorities=new Dictionary<string,int>();var stack=new Stack<string>(roots.AsEnumerable().Reverse());
            while(stack.TryPop(out var id))
            {
                if(!priorities.TryAdd(id,priorities.Count))continue;
                var next=outgoing[id].OrderBy(p=>Rank(p.Value)).ThenBy(p=>items[p.Key].Y).ThenBy(p=>items[p.Key].X).ThenBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key).ToArray();
                for(int i=next.Length-1;i>=0;i--)stack.Push(next[i]);
            }
            foreach(var id in Spatial(ids))priorities.TryAdd(id,priorities.Count);
            var ready=new SortedSet<(int Rank,string Id)>(ids.Where(id=>indegree[id]==0).Select(id=>(priorities[id],id)));
            var result=new List<string>();
            while(ready.Count>0)
            {
                var current=ready.Min;ready.Remove(current);result.Add(current.Id);
                foreach(var target in outgoing[current.Id].Keys)if(--indegree[target]==0)ready.Add((priorities[target],target));
            }
            var unresolved=ids.Except(result).ToArray();
            if(unresolved.Length>0)
            {
                blocked.UnionWith(unresolved);
                var unresolvedSet=unresolved.ToHashSet();var state=new Dictionary<string,int>();Link? bad=null;
                foreach(var seed in unresolved)
                {
                    if(state.ContainsKey(seed))continue;
                    var search=new Stack<(string Id,bool Exit)>();search.Push((seed,false));
                    while(search.TryPop(out var step)&&bad==null)
                    {
                        if(step.Exit){state[step.Id]=2;continue;}
                        if(state.ContainsKey(step.Id))continue;
                        state[step.Id]=1;search.Push((step.Id,true));
                        foreach(var target in outgoing[step.Id].Keys.Where(unresolvedSet.Contains))
                        {
                            if(state.GetValueOrDefault(target)==1){bad=local.First(l=>l.From==step.Id&&l.To==target);break;}
                            if(!state.ContainsKey(target))search.Push((target,false));
                        }
                    }
                    if(bad!=null)break;
                }
                var detail=bad==null?"":" Check “"+items[bad.From].Name+"” → “"+items[bad.To].Name+"”.";
                issues.Add(new("cycle",ScopeName()+" has a cycle or conflicting order across groups. "+unresolved.Length+" affected objects, including downstream objects, are omitted until resolved."+detail,bad?.Edge.Id??unresolved[0]));
            }
            ordered[scope]=result;
        }
        var pieces=new List<FullTextPiece>();int pendingBreak=0;var emitted=new HashSet<string>();
        var work=new Stack<(string Id,bool Exit)>();
        foreach(var id in (ordered.GetValueOrDefault("")??[]).AsEnumerable().Reverse())work.Push((id,false));
        while(work.TryPop(out var step))
        {
            var item=items[step.Id];
            if(item.Kind!="point")
            {
                pendingBreak=Math.Max(pendingBreak,item.Kind=="frame"?2:1);
                if(!step.Exit)
                {
                    work.Push((item.Id,true));
                    foreach(var child in (ordered.GetValueOrDefault(item.Id)??[]).AsEnumerable().Reverse())work.Push((child,false));
                }
                continue;
            }
            if(!connectedPoints.Contains(item.Id)||!emitted.Add(item.Id))continue;
            var body=item.Body.Trim();
            if(body.Length==0){issues.Add(new("empty","“"+item.Name+"” is connected to the body text but has no content.",item.Id));continue;}
            var path=Path(item.Id);string? frame=path.LastOrDefault(id=>items[id].Kind=="frame"),circle=path.LastOrDefault(id=>items[id].Kind=="circle");
            string separator=pieces.Count==0?"":new string('\n',pendingBreak);
            if(separator.Length==0&&pieces.Count>0&&NeedsSpace(pieces[^1].Text[^1],body[0]))separator=" ";
            pieces.Add(new(item.Id,body,separator,frame,circle));pendingBreak=0;
        }
        var blockedPoints=connectedPoints.Count(id=>Path(id).Any(blocked.Contains));
        var text=new StringBuilder();foreach(var piece in pieces)text.Append(piece.Separator).Append(piece.Text);
        return new(pageEdges[0].Id,text.ToString(),pieces,issues,pageEdges.SelectMany(e=>new[]{e.From,e.To}).Distinct().ToArray(),
            pageEdges.Select(e=>e.Id).ToArray(),blockedPoints);
        }
    }
    static bool NeedsSpace(char a,char b)=>a<256&&b<256&&!char.IsWhiteSpace(a)&&!char.IsWhiteSpace(b)&&
        (char.IsLetterOrDigit(a)||a is '.' or ',' or ';' or ':' or '!' or '?' or ')')&&(char.IsLetterOrDigit(b)||b=='(');
}
