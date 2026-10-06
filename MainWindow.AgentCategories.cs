using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Papergraph;

public partial class MainWindow
{
    object AgentManageCategories(JsonElement request,string operation)
    {
        var requestId=AgentProtocol.Required(request,"requestId");
        if(!Guid.TryParse(requestId,out var guid))throw new InvalidDataException("requestId must be a UUID.");
        var key=guid.ToString("N");var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.GetRawText())));
        object Result(bool replay)=>new{ok=true,requestId,alreadyApplied=replay,libraryRevision=catalog.Revision(),categoryId=operation=="createCategory"?key:request.TryGetProperty("categoryId",out var c)&&c.ValueKind==JsonValueKind.String?c.GetString():null,
            categories=catalog.Categories.Select(c=>new{id=c.Id,title=c.Name}).ToArray()};
        if(catalog.Receipts.TryGetValue(key,out var prior))
        {
            if(prior!=hash)throw new InvalidDataException("requestId was already used for a different category operation.");
            return Result(true);
        }
        if(AgentProtocol.Required(request,"expectedLibraryRevision")!=catalog.Revision())throw new InvalidDataException("Library categories changed. Read listGraphs again.");
        // Never overwrite an externally modified or unreadable catalog.
        if(File.Exists(CatalogPath)&&GraphLibraryCatalog.Parse(File.ReadAllText(CatalogPath)).Revision()!=catalog.Revision())throw new InvalidDataException("Category file changed outside this app. Reopen the library before editing categories.");
        var next=catalog.Copy();
        string Name()
        {
            var name=AgentProtocol.Required(request,"title").Trim();
            if(name.Length>200)throw new InvalidDataException("Category names support up to 200 characters.");
            return name;
        }
        string CategoryId(bool nullable=false)
        {
            if(nullable&&request.TryGetProperty("categoryId",out var v)&&(v.ValueKind==JsonValueKind.Null||v.ValueKind==JsonValueKind.String&&v.GetString()==""))return "";
            var id=AgentProtocol.Required(request,"categoryId");
            if(!next.Categories.Any(c=>c.Id==id))throw new InvalidDataException("Unknown categoryId. Read listGraphs.");return id;
        }
        if(operation=="createCategory")next.Categories.Add(new(){Id=key,Name=Name()});
        else if(operation=="renameCategory"){var id=CategoryId();next.Categories.First(c=>c.Id==id).Name=Name();}
        else if(operation=="removeCategory")
        {
            var id=CategoryId();next.Categories.RemoveAll(c=>c.Id==id);
            foreach(var path in next.Assignments.Where(a=>a.Value==id).Select(a=>a.Key).ToArray())next.Assignments.Remove(path);
        }
        else if(operation=="moveGraphs")
        {
            var id=CategoryId(true);
            if(!request.TryGetProperty("documentPaths",out var paths)||paths.ValueKind!=JsonValueKind.Array||paths.GetArrayLength() is <1 or >100)throw new InvalidDataException("Supply 1–100 documentPaths from listGraphs.");
            foreach(var value in paths.EnumerateArray())
            {
                var path=AgentLibraryPath(value.GetString()??throw new InvalidDataException("Invalid document path."));
                if(id.Length==0)next.Assignments.Remove(path);else next.Assignments[path]=id;
            }
        }
        else throw new InvalidDataException("Unknown category operation.");
        if(next.Categories.GroupBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidDataException("A category with that name already exists.");
        if(next.Receipts.Count>=256)next.Receipts.Remove(next.Receipts.Keys.First());next.Receipts[key]=hash;
        next.Save(CatalogPath);catalog=next;RefreshLibraryTree(operation=="moveGraphs");Notify(operation=="removeCategory"?"Category removed · graphs kept in Unfiled":"Graph categories updated");return Result(false);
    }
}
