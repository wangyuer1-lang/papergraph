namespace Papergraph;

public enum ConnectionDisplay { All, WithinFrames, AcrossFrames }

public static class GraphConnections
{
    public static HashSet<string> MatchingIds(GraphDocument doc, ConnectionDisplay display)
    {
        if(display==ConnectionDisplay.All)return doc.Edges.Select(e=>e.Id).ToHashSet();
        var membership=doc.Nodes.Select(n=>n.Id).Concat(doc.Regions.Select(r=>r.Id)).ToDictionary(id=>id,_=>new HashSet<string>());
        var contents=new Dictionary<string,HashSet<string>>();
        foreach(var frame in doc.Regions)
        {
            // Use the same membership as entering a frame, including the contents of rings.
            // Logical positions, rather than screen-sized ring padding, keep this stable at every zoom.
            var roots=frame.IsAbsolute?GraphBoard.Members(doc,frame).Select(n=>n.Id):frame.Members;
            contents[frame.Id]=doc.Descendants(roots);
            foreach(var id in contents[frame.Id])if(membership.TryGetValue(id,out var frames))frames.Add(frame.Id);
        }
        // A shared wrapper must not turn links between its separate inner frames into Within.
        // Keep all innermost memberships: overlapping, non-nested frames remain valid together.
        var inner=doc.Regions.ToDictionary(r=>r.Id,r=>GraphBoard.InnerRegions(doc,r).Select(x=>x.Id)
            .Concat(doc.Regions.Where(x=>x.Id!=r.Id&&x.Parent!=null&&contents[r.Id].Contains(x.Parent)).Select(x=>x.Id)).ToHashSet());
        foreach(var frames in membership.Values)
            frames.ExceptWith(frames.Where(id=>inner[id].Overlaps(frames)).ToArray());
        foreach(var frame in doc.Regions)membership[frame.Id]=[frame.Id];
        var result=new HashSet<string>();
        foreach(var edge in doc.Edges)
        {
            if(!membership.TryGetValue(edge.From,out var a)||!membership.TryGetValue(edge.To,out var b))continue;
            bool within=a.Overlaps(b);
            bool across=!within&&(a.Count>0||b.Count>0);
            if(display==ConnectionDisplay.WithinFrames?within:across)result.Add(edge.Id);
        }
        return result;
    }
}
