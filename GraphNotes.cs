using System.IO;
using System.Text.Json;
namespace Papergraph;

public interface IGraphNotes
{
    string Note { get; set; }
    List<NotePage> AdditionalNotes { get; set; }
}

public sealed class NotePage
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}

internal static class GraphNotes
{
    internal const int MaxAdditionalPages = 3;
    internal static string AllText(IGraphNotes item) => string.Join("\n", new[]{item.Note}.Concat(item.AdditionalNotes
        .Select(p=>string.Join("\n",new[]{p.Title,p.Body}.Where(s=>s.Length>0)))).Where(s=>s.Length>0));
    internal static object[] Snapshot(IGraphNotes item) => new object[]{new{page=1,title="",body=item.Note,agentWritable=false}}
        .Concat(item.AdditionalNotes.Select((p,i)=>(object)new{page=i+2,title=p.Title,body=p.Body,agentWritable=true})).ToArray();
    internal static void Validate(IGraphNotes item)
    {
        if(item.AdditionalNotes==null||item.AdditionalNotes.Count>MaxAdditionalPages||item.AdditionalNotes.Any(p=>p==null||p.Title==null||p.Body==null))
            throw new InvalidDataException("Invalid note pages.");
        while(item.AdditionalNotes.Count<MaxAdditionalPages)item.AdditionalNotes.Add(new());
    }
    internal static void ApplyAgent(IGraphNotes item, JsonElement input)
    {
        Validate(item);
        // Only an explicitly requested, lossless relocation may clear legacy page 1.
        // Ordinary note writes below remain unable to modify it.
        if(input.TryGetProperty("moveFirstPage",out var move))
        {
            if(input.TryGetProperty("note",out _)||input.TryGetProperty("notePages",out _))throw new InvalidDataException("Relocate notes separately from note edits.");
            if(!move.TryGetProperty("expectedBody",out var expected)||expected.ValueKind!=JsonValueKind.String||string.IsNullOrEmpty(item.Note)||expected.GetString()!=item.Note)
                throw new InvalidDataException("First-page content changed. Read and review it again before moving.");
            if(!move.TryGetProperty("targetPage",out var destination)||!destination.TryGetInt32(out var page)||page<2||page>4)
                throw new InvalidDataException("Move notes only to page 2, 3 or 4.");
            var target=item.AdditionalNotes[page-2];
            if(target.Body.Length!=0)throw new InvalidDataException("Destination note page must be empty.");
            target.Body=item.Note;item.Note="";return;
        }
        // The legacy 'note' input now addresses page 2, never the human page.
        if(input.TryGetProperty("note",out var legacy))
        {
            var text=legacy.GetString()??throw new InvalidDataException("note cannot be null.");
            if(item.AdditionalNotes.Count==0)item.AdditionalNotes.Add(new());
            item.AdditionalNotes[0].Body=text;
        }
        if(!input.TryGetProperty("notePages",out var pages))return;
        if(pages.ValueKind!=JsonValueKind.Array||pages.GetArrayLength()>MaxAdditionalPages)throw new InvalidDataException("Invalid notePages array.");
        var seen=new HashSet<int>();
        foreach(var p in pages.EnumerateArray())
        {
            if(!p.TryGetProperty("page",out var index)||!index.TryGetInt32(out var page)||page<2)
                throw new InvalidDataException("Page 1 is reserved for human editing. Agents may write only pages 2 and later.");
            if(page>MaxAdditionalPages+1||page>item.AdditionalNotes.Count+2||!seen.Add(page))throw new InvalidDataException("Append note pages in order, without gaps or duplicates.");
            if(page==item.AdditionalNotes.Count+2)item.AdditionalNotes.Add(new());
            var target=item.AdditionalNotes[page-2];
            if(p.TryGetProperty("title",out var title))target.Title=title.GetString()??throw new InvalidDataException("Note title cannot be null.");
            if(p.TryGetProperty("body",out var body))target.Body=body.GetString()??throw new InvalidDataException("Note body cannot be null.");
        }
        Validate(item);
    }
}
