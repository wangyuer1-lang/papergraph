using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Papergraph;

public interface IGraphBody
{
    string Title { get; set; }
    List<BodyTextColor>? BodyColors { get; set; }
}
public sealed record BodyTextColor(int Start,int Length,string Color)
{
    [System.Text.Json.Serialization.JsonIgnore] public int End=>Start+Length;
}

// UTF-16 ranges match native text selections. Formatting never changes manuscript text.
public static class GraphTextColors
{
    public static readonly (string Name,string Color)[] Palette=[("Red","#D44747"),("Orange","#B87516"),("Green","#2B9464"),("Blue","#397FC4"),("Purple","#9A63BE")];
    public static bool Boundary(string text,int offset)=>offset>=0&&offset<=text.Length&&!(offset>0&&offset<text.Length&&char.IsHighSurrogate(text[offset-1])&&char.IsLowSurrogate(text[offset]));
    public static void Validate(string text,List<BodyTextColor>? ranges)
    {
        if(ranges==null)return;
        if(ranges.Count>5000)throw new InvalidDataException("Too many text color ranges.");
        int end=0;
        foreach(var r in ranges)
        {
            if(r==null||r.Start<end||r.Length<=0||r.Start<0||r.Start>text.Length||r.Length>text.Length-r.Start||!Boundary(text,r.Start)||!Boundary(text,r.End)||r.Color==null||!Regex.IsMatch(r.Color,"^#[0-9a-fA-F]{6}$"))throw new InvalidDataException("Invalid text color range.");
            end=r.End;
        }
    }
    static List<BodyTextColor>? Normalize(IEnumerable<BodyTextColor> ranges)
    {
        var result=new List<BodyTextColor>();
        foreach(var r in ranges.Where(r=>r.Length>0).OrderBy(r=>r.Start))
        {
            var item=r with {Color=r.Color.ToUpperInvariant()};
            if(result.Count>0&&result[^1].End==item.Start&&result[^1].Color==item.Color)result[^1]=result[^1] with {Length=result[^1].Length+item.Length};else result.Add(item);
        }
        return result.Count==0?null:result;
    }
    public static List<BodyTextColor>? Apply(string text,List<BodyTextColor>? ranges,int start,int length,string? color)
    {
        if(length<=0||start<0||start>text.Length||length>text.Length-start||!Boundary(text,start)||!Boundary(text,start+length))throw new InvalidDataException("Select text to color.");
        if(color!=null&&!Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$"))throw new InvalidDataException("Invalid text color.");
        int end=start+length;var next=new List<BodyTextColor>();
        foreach(var r in ranges??[])
        {
            if(r.End<=start||r.Start>=end){next.Add(r);continue;}
            if(r.Start<start)next.Add(r with {Length=start-r.Start});
            if(r.End>end)next.Add(new(end,r.End-end,r.Color));
        }
        if(color!=null)next.Add(new(start,length,color));
        return Normalize(next);
    }
    public static List<BodyTextColor>? Adjust(string oldText,string newText,List<BodyTextColor>? ranges)
    {
        if(ranges==null||oldText==newText)return ranges;
        int start=0;
        while(start<oldText.Length&&start<newText.Length&&oldText[start]==newText[start])start++;
        while(start>0&&(!Boundary(oldText,start)||!Boundary(newText,start)))start--;
        int oldEnd=oldText.Length,newEnd=newText.Length;
        while(oldEnd>start&&newEnd>start&&oldText[oldEnd-1]==newText[newEnd-1]){oldEnd--;newEnd--;}
        while(!Boundary(oldText,oldEnd)||!Boundary(newText,newEnd)){oldEnd++;newEnd++;}
        int delta=newEnd-oldEnd;var next=new List<BodyTextColor>();
        foreach(var r in ranges)
        {
            if(r.End<=start){next.Add(r);continue;}
            if(r.Start>=oldEnd){next.Add(r with {Start=r.Start+delta});continue;}
            if(r.Start<start)next.Add(r with {Length=start-r.Start});
            if(newEnd>start&&r.Start<=start&&r.End>=oldEnd)next.Add(new(start,newEnd-start,r.Color));
            if(r.End>oldEnd)next.Add(new(newEnd,r.End-oldEnd,r.Color));
        }
        return Normalize(next);
    }
    public static void ApplyAgent(IGraphBody item,JsonElement request)
    {
        if(!request.TryGetProperty("bodyColors",out var colors))return;
        if(colors.ValueKind!=JsonValueKind.Array||colors.GetArrayLength()>5000)throw new InvalidDataException("bodyColors must be an array of at most 5000 ranges.");
        var ranges=new List<BodyTextColor>();
        foreach(var entry in colors.EnumerateArray())
        {
            var r=new BodyTextColor(entry.GetProperty("start").GetInt32(),entry.GetProperty("length").GetInt32(),entry.GetProperty("color").GetString()??"");
            ranges.Add(r);
            Validate(item.Title,ranges);
            if(entry.TryGetProperty("text",out var quote)&&quote.GetString()!=item.Title.Substring(r.Start,r.Length))throw new InvalidDataException("Text color selection no longer matches the requested text.");
        }
        item.BodyColors=Normalize(ranges);
    }
    public static object[] Snapshot(IGraphBody item)=>(item.BodyColors??[]).Select(r=>(object)new{start=r.Start,length=r.Length,color=r.Color,text=item.Title.Substring(r.Start,r.Length)}).ToArray();
    public static IEnumerable<(string Text,string? Color)> Segments(IGraphBody? item,string displayedText)
    {
        int offset=item?.Title.IndexOf(displayedText,StringComparison.Ordinal)??-1;
        if(offset<0||item?.BodyColors is not {Count:>0}){yield return(displayedText,null);yield break;}
        int position=0;
        foreach(var r in item.BodyColors)
        {
            int start=Math.Max(0,r.Start-offset),end=Math.Min(displayedText.Length,r.End-offset);
            if(end<=start)continue;
            if(start>position)yield return(displayedText[position..start],null);
            yield return(displayedText[start..end],r.Color);position=end;
        }
        if(position<displayedText.Length)yield return(displayedText[position..],null);
    }
}
