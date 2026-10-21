namespace Papergraph;

// Keep source boundaries while a user types. The read projection trims bodies and
// inserts separators, so splitting the displayed text by whitespace is not safe.
internal sealed class FullTextEditBuffer
{
    // Citation backgrounds are display-only, separate from saved text colors.
    // Lemon is deliberately outside the red/orange/green/blue/purple text palette.
    public static string ReferenceHighlightColor(bool dark)=>dark?"#3C3B20":"#F5F2B5";
    internal sealed record Span(string NodeId,int Start,int End);
    internal sealed record Edit(int Index,string NodeId,string Text,string Body);
    sealed class Part(string id,string separator,string text,string body,string after="",string? host=null,int referenceIndex=0,int referenceCount=0)
    {
        public readonly string Id=id;
        public string Separator=separator,After=after;
        public readonly string? Host=host;
        public readonly int ReferenceIndex=referenceIndex,ReferenceCount=referenceCount;
        public readonly string Prefix=body[..(body.Length-body.TrimStart().Length)];
        public readonly string Suffix=body[(body.TrimEnd().Length)..];
        public string Text=text,Body=body;
    }
    readonly List<Part> parts;
    public string PageId {get;}
    public string Text=>string.Concat(parts.Select(p=>p.Separator+p.Text+p.After));
    public IReadOnlyList<Span> Spans
    {
        get
        {
            int offset=0;var result=new List<Span>();
            foreach(var p in parts){offset+=p.Separator.Length;result.Add(new(p.Id,offset,offset+p.Text.Length));offset+=p.Text.Length+p.After.Length;}
            return result;
        }
    }
    public FullTextEditBuffer(GraphDocument doc,FullTextPage page)
    {
        PageId=page.Id;
        parts=[];
        foreach(var p in page.Pieces)
        {
            parts.Add(new(p.NodeId,p.Separator,p.Text,doc.Node(p.NodeId)!.Title));
            for(int i=0;i<p.References.Count;i++){
                var r=p.References[i];
                parts.Add(new(r.NodeId,i==0?p.ReferenceOpening:p.ReferenceSeparator,r.Text,doc.Node(r.NodeId)!.Title,i==p.References.Count-1?p.ReferenceClosing:"",p.NodeId,i,p.References.Count));
            }
        }
    }
    public bool Matches(GraphDocument doc)=>parts.All(p=>doc.Node(p.Id)?.Title==p.Body);
    public Span? At(int caret,string? preferred=null)
    {
        var candidates=Spans.Where(p=>caret>=p.Start&&caret<=p.End).ToArray();
        return candidates.FirstOrDefault(p=>p.NodeId==preferred)
            ??candidates.FirstOrDefault(p=>caret==p.Start)??candidates.LastOrDefault();
    }
    public bool TryPrepare(string next,int selectionStart,int selectionEnd,string? preferred,out Edit? edit)
    {
        edit=null;var old=Text;if(old==next)return true;
        int start=Math.Clamp(Math.Min(selectionStart,selectionEnd),0,old.Length);
        int end=Math.Clamp(Math.Max(selectionStart,selectionEnd),start,old.Length);
        int inserted=next.Length-(old.Length-(end-start));
        // Prefer the actual selection: repeated text at a proposition boundary
        // otherwise makes a longest-common-prefix diff choose the wrong source.
        if(inserted<0||!next.StartsWith(old[..start],StringComparison.Ordinal)||!next.EndsWith(old[end..],StringComparison.Ordinal))
        {
            start=0;while(start<old.Length&&start<next.Length&&old[start]==next[start])start++;
            while(start>0&&(!GraphTextColors.Boundary(old,start)||!GraphTextColors.Boundary(next,start)))start--;
            end=old.Length;int newEnd=next.Length;
            while(end>start&&newEnd>start&&old[end-1]==next[newEnd-1]){end--;newEnd--;}
            while(!GraphTextColors.Boundary(old,end)||!GraphTextColors.Boundary(next,newEnd)){end++;newEnd++;}
            inserted=newEnd-start;
        }
        if(!GraphTextColors.Boundary(old,start)||!GraphTextColors.Boundary(old,end)
            ||!GraphTextColors.Boundary(next,start)||!GraphTextColors.Boundary(next,start+inserted))return false;
        var spans=Spans;
        var candidates=spans.Where(p=>start>=p.Start&&end<=p.End).ToArray();
        var span=candidates.FirstOrDefault(p=>p.NodeId==preferred)
            ??candidates.FirstOrDefault(p=>p.Start==start)??candidates.LastOrDefault();
        // A replacement must belong to one proposition. Never guess which node
        // should own a replacement spanning several sources or a generated break.
        if(span==null)return false;
        int index=spans.ToList().IndexOf(span);var part=parts[index];
        string text=part.Text[..(start-span.Start)]+next.Substring(start,inserted)+part.Text[(end-span.Start)..];
        edit=new(index,part.Id,text,part.Prefix+text+part.Suffix);return true;
    }
    public int Commit(Edit edit)
    {
        int start=Spans[edit.Index].Start;
        foreach(var part in parts.Where(p=>p.Id==edit.NodeId)){part.Text=edit.Text;part.Body=edit.Body;}
        Part? previous=null;
        foreach(var part in parts.Where(p=>p.Host==null)){
            if(previous!=null&&previous.Text.Length>0&&part.Text.Length>0&&!part.Separator.Contains('\n'))
                part.Separator=GraphFullText.NeedsSpace(previous.Text[^1],part.Text[0])?" ":"";
            previous=part;
        }
        foreach(var part in parts.Where(p=>p.Host==edit.NodeId)){
            var format=new FullTextPiece(edit.NodeId,edit.Text,"",null,null);
            part.Separator=part.ReferenceIndex==0?format.ReferenceOpening:format.ReferenceSeparator;
            part.After=part.ReferenceIndex==part.ReferenceCount-1?format.ReferenceClosing:"";
        }
        return Spans[edit.Index].Start-start;
    }
    public IReadOnlyList<BodyTextColor> Colors(GraphDocument doc)
    {
        var colors=new List<BodyTextColor>();int offset=0;
        foreach(var part in parts)
        {
            offset+=part.Separator.Length;
            foreach(var c in doc.Node(part.Id)?.BodyColors??[])
            {
                int start=Math.Max(0,c.Start-part.Prefix.Length),end=Math.Min(part.Text.Length,c.End-part.Prefix.Length);
                if(end>start)colors.Add(new(offset+start,end-start,c.Color));
            }
            offset+=part.Text.Length+part.After.Length;
        }
        return colors;
    }
    public IReadOnlyList<BodyTextColor> ReferenceHighlights(bool dark)
    {
        var ranges=new List<BodyTextColor>();int offset=0;
        foreach(var part in parts)
        {
            int end=offset+part.Separator.Length+part.Text.Length+part.After.Length;
            if(part.Host!=null)
            {
                // Include the generated brackets and inter-reference separator,
                // but leave the space before an English opening bracket plain.
                int start=offset+part.Separator.Length-part.Separator.TrimStart().Length;
                if(end>start)ranges.Add(new(start,end-start,ReferenceHighlightColor(dark)));
            }
            offset=end;
        }
        return ranges;
    }
}
