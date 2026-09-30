namespace Papergraph;

public enum ConnectionDisplay { All, WithinFrames, AcrossFrames }

public static class GraphConnections
{
    public static HashSet<string> MatchingIds(GraphDocument doc, ConnectionDisplay display)
    {
        if(display==ConnectionDisplay.All)return doc.Edges.Select(e=>e.Id).ToHashSet();
        var membership=doc.Nodes.ToDictionary(n=>n.Id,_=>new HashSet<string>());
        foreach(var frame in doc.Regions)
        {
            // Use the same membership as entering a frame, including the contents of rings.
            // Logical positions, rather than screen-sized ring padding, keep this stable at every zoom.
            var roots=frame.IsAbsolute?GraphBoard.Members(doc,frame).Select(n=>n.Id):frame.Members;
            foreach(var id in doc.Descendants(roots))if(membership.TryGetValue(id,out var frames))frames.Add(frame.Id);
        }
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
