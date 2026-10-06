using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Papergraph;

public abstract class LibraryRow : INotifyPropertyChanged
{
    string title=""; bool selected,expanded=true;
    public string Title {get=>title;set{title=value;Changed(nameof(Title));}}
    public bool IsSelected {get=>selected;set{selected=value;Changed(nameof(IsSelected));}}
    public bool IsExpanded {get=>expanded;set{expanded=value;Changed(nameof(IsExpanded));}}
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed(string name)=>PropertyChanged?.Invoke(this,new(name));
}
public sealed class GraphEntry : LibraryRow
{
    public GraphEntry(string path,string title){Path=path;Title=title;}
    public string Path {get;}
    public string CategoryId {get;set;}="";
}
public sealed class GraphCategory : LibraryRow
{
    public GraphCategory(string id,string title){Id=id;Title=title;}
    public string Id {get;}
    public ObservableCollection<GraphEntry> Graphs {get;}=[];
}
public sealed class LibraryCategory
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
}
public sealed class GraphLibraryCatalog
{
    public List<LibraryCategory> Categories {get;set;}=[];
    public Dictionary<string,string> Assignments {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,string> Receipts {get;set;}=[];
    public string Revision()=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Categories,Assignments}))));
    public GraphLibraryCatalog Copy()=>Parse(JsonSerializer.Serialize(this));
    public static GraphLibraryCatalog Parse(string json)
    {
        var value=JsonSerializer.Deserialize<GraphLibraryCatalog>(json)??throw new InvalidDataException("Empty library categories.");
        if(value.Categories==null||value.Assignments==null||value.Receipts==null||value.Categories.Count>1000)throw new InvalidDataException("Invalid library categories.");
        var ids=new HashSet<string>();
        foreach(var c in value.Categories)if(c==null||string.IsNullOrWhiteSpace(c.Id)||!ids.Add(c.Id)||string.IsNullOrWhiteSpace(c.Name)||c.Name.Length>200)throw new InvalidDataException("Invalid category.");
        if(value.Assignments.Any(a=>!Path.IsPathFullyQualified(a.Key)||!ids.Contains(a.Value)))throw new InvalidDataException("Invalid graph category assignment.");
        value.Assignments=new(value.Assignments,StringComparer.OrdinalIgnoreCase);return value;
    }
    public void Save(string path)
    {
        Parse(JsonSerializer.Serialize(this));
        var pending=path+".tmp";
        using(var stream=new FileStream(pending,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
        {var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this,GraphDocument.Options));stream.Write(bytes);stream.Flush(true);}
        if(File.Exists(path))File.Copy(path,path+".bak",true);
        File.Move(pending,path,true);
    }
}
