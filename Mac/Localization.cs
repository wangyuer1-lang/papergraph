using System.Globalization;
using System.Text.Json;
namespace Papergraph;
public static class Localization
{
    static readonly Dictionary<string,Dictionary<string,string>> catalogs=new[]{"en","zh-CN","ja"}.ToDictionary(c=>c,c=>{
        using var stream=typeof(Localization).Assembly.GetManifestResourceStream("Papergraph.Locales."+c+".json")!;
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    });
    public static readonly (string Code,string Name)[] Languages=[("en","English"),("zh-CN","简体中文"),("ja","日本語")];
    public static string Language{get;set;}=Normalize(CultureInfo.CurrentUICulture.Name);
    public static string Normalize(string? code)=>code?.StartsWith("zh",StringComparison.OrdinalIgnoreCase)==true?"zh-CN":code?.StartsWith("ja",StringComparison.OrdinalIgnoreCase)==true?"ja":"en";
    public static string Text(string source,string? language=null)=>catalogs[language??Language].GetValueOrDefault(source)??source;
}
