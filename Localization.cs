using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace Papergraph;

// English source strings are stable keys. Missing translations fall back to English.
public static class Localization
{
    static readonly Dictionary<string, Dictionary<string,string>> catalogs = Load();
    public static readonly (string Code,string Name)[] Languages = [("en","English"),("zh-CN","简体中文"),("ja","日本語")];
    public static string Language { get; private set; } = "en";
    public static IEnumerable<string> Keys => catalogs["en"].Keys;
    static Dictionary<string, Dictionary<string,string>> Load()
    {
        var result = new Dictionary<string, Dictionary<string,string>>();
        foreach(var code in new[]{"en","zh-CN","ja"})
        {
            using var stream=typeof(Localization).Assembly.GetManifestResourceStream("Papergraph.Locales."+code+".json") ?? throw new InvalidOperationException("Missing language: "+code);
            result[code]=JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
        }
        return result;
    }
    public static string Normalize(string? code)
    {
        if(code?.StartsWith("zh",StringComparison.OrdinalIgnoreCase)==true)return "zh-CN";
        if(code?.StartsWith("ja",StringComparison.OrdinalIgnoreCase)==true)return "ja";
        return "en";
    }
    public static string SystemLanguage => Normalize(CultureInfo.CurrentUICulture.Name);
    public static string Key(string source) => "Loc"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
    public static string Text(string source,string? language=null)
    {
        var code=language==null?Language:Normalize(language);
        return catalogs[code].GetValueOrDefault(source) ?? source;
    }
    public static string Format(string source,params object[] values) => string.Format(CultureInfo.CurrentCulture,Text(source),values);
    public static void Apply(ResourceDictionary resources,string code)
    {
        Language=Normalize(code);
        foreach(var source in Keys)resources[Key(source)]=Text(source);
    }
    public static void Bind(FrameworkElement element,DependencyProperty property,string source) => element.SetResourceReference(property,Key(source));
}
