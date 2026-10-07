using System.IO;
namespace Papergraph;

// Unix libraries may be on case-sensitive volumes. Never collapse distinct paths there.
internal static class PathIdentity
{
    internal static StringComparer Comparer=>OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
    internal static StringComparison Comparison=>OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
    internal static string Normalize(string path)
    {
        var full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        return OperatingSystem.IsWindows()?full.ToLowerInvariant():full;
    }
    internal static bool Same(string a,string b)=>string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),Comparison);
}
