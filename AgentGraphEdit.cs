using System.IO;
using System.Text.Json;
namespace Papergraph;

// Explicit, atomic graph edits. Never accepts arbitrary code or file paths to write.
internal static class AgentGraphEdit
{
    internal static GraphDocument Prepare(GraphDocument current,string file,JsonElement request)
    {
        if(!PathIdentity.Same(AgentProtocol.Required(request,"expectedDocument"),file))throw new InvalidDataException("Active document changed. Read a fresh snapshot.");
        if(AgentProtocol.Required(request,"expectedRevision")!=AgentProtocol.Revision(current))throw new InvalidDataException("Document changed. Read a fresh snapshot before editing.");
        if(!Guid.TryParse(AgentProtocol.Required(request,"requestId"),out _))throw new InvalidDataException("requestId must be a UUID.");
        var next=GraphDocument.Parse(current.Serialize());int count=0;
        IEnumerable<JsonElement> Items(string name)
        {
            if(!request.TryGetProperty(name,out var a))return [];
            if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()>100)throw new InvalidDataException("Each edit array supports at most 100 objects.");
            count+=a.GetArrayLength();return a.EnumerateArray().ToArray();
        }
        string Text(JsonElement e,string key,string old)=>e.TryGetProperty(key,out var v)?v.GetString()??throw new InvalidDataException(key+" cannot be null."):old;
        double Number(JsonElement e,string key,double old)=>e.TryGetProperty(key,out var v)?v.GetDouble():old;
        var used=new HashSet<string>();
        string Id(JsonElement e){var id=AgentProtocol.Required(e,"id");if(!used.Add(id))throw new InvalidDataException("Duplicate edit id.");return id;}
        foreach(var e in Items("nodes"))
        {
            var id=Id(e);var n=next.Node(id);
            if(n==null){if(!e.TryGetProperty("x",out _)||!e.TryGetProperty("y",out _))throw new InvalidDataException("New nodes require x and y.");n=new(){Id=id};next.Nodes.Add(n);}
            n.Title=Text(e,"body",n.Title);n.Caption=Text(e,"caption",n.Caption);GraphNotes.ApplyAgent(n,e);n.Color=Text(e,"color",n.Color);
            if(e.TryGetProperty("markColor",out var mark))n.MarkColor=mark.ValueKind==JsonValueKind.Null?null:mark.GetString();
            n.X=Number(e,"x",n.X);n.Y=Number(e,"y",n.Y);
            if(e.TryGetProperty("parentId",out var parent))n.Parent=parent.ValueKind==JsonValueKind.Null?null:parent.GetString();
        }
        foreach(var e in Items("regions"))
        {
            var id=Id(e);var r=next.Regions.Find(r=>r.Id==id);
            if(r==null){foreach(var k in new[]{"x","y","width","height"})if(!e.TryGetProperty(k,out _))throw new InvalidDataException("New regions require bounds.");r=new(){Id=id,IsAbsolute=true};next.Regions.Add(r);}
            r.Title=Text(e,"body",r.Title);r.Caption=Text(e,"caption",r.Caption);GraphNotes.ApplyAgent(r,e);r.Color=Text(e,"color",r.Color);
            r.X=Number(e,"x",r.X);r.Y=Number(e,"y",r.Y);r.Width=Number(e,"width",r.Width);r.Height=Number(e,"height",r.Height);
            if(e.TryGetProperty("parentId",out var parent))r.Parent=parent.ValueKind==JsonValueKind.Null?null:parent.GetString();
        }
        foreach(var e in Items("edges"))
        {
            var id=Id(e);var edge=next.Edges.Find(x=>x.Id==id);
            if(edge==null){edge=new(){Id=id,From=AgentProtocol.Required(e,"from"),To=AgentProtocol.Required(e,"to")};next.Edges.Add(edge);}
            edge.From=Text(e,"from",edge.From);edge.To=Text(e,"to",edge.To);edge.Label=Text(e,"kind",edge.Label);
            edge.Caption=Text(e,"caption",edge.Caption);GraphNotes.ApplyAgent(edge,e);edge.Direction=Text(e,"direction",edge.Direction);
            if(e.TryGetProperty("textRole",out var role))edge.TextRole=role.GetString() is string value&&value!="flow"?value:null;
            if(e.TryGetProperty("textOrder",out var order))edge.TextOrder=order.GetInt32();
            if(e.TryGetProperty("markColor",out var mark))edge.MarkColor=mark.ValueKind==JsonValueKind.Null?null:mark.GetString();
        }
        foreach(var e in Items("groups"))
        {
            var id=Id(e);if(next.Node(id)!=null)throw new InvalidDataException("New group id already exists.");
            var members=e.GetProperty("members").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
            if(members.Length<2||members.Distinct().Count()!=members.Length)throw new InvalidDataException("A group needs distinct connected members.");
            var first=next.Node(members[0])??throw new InvalidDataException("Missing group member.");
            var group=next.Collapse(members,first.Parent);var temporary=group.Id;group.Id=id;
            foreach(var n in next.Nodes.Where(n=>n.Parent==temporary))n.Parent=id;
            foreach(var r in next.Regions){if(r.Parent==temporary)r.Parent=id;for(int i=0;i<r.Members.Count;i++)if(r.Members[i]==temporary)r.Members[i]=id;}
            group.Caption=Text(e,"caption","");group.Title=Text(e,"body","");GraphNotes.ApplyAgent(group,e);group.Color=Text(e,"color",group.Color);
            if(e.TryGetProperty("markColor",out var mark))group.MarkColor=mark.ValueKind==JsonValueKind.Null?null:mark.GetString();
        }
        if(count==0)throw new InvalidDataException("No edits supplied.");next.Validate();return next;
    }
}
