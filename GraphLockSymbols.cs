namespace Papergraph;

// One vector silhouette for both desktop targets; no emoji/font dependency.
internal static class GraphLockSymbols
{
    const string Body="M6,10 H18 Q20,10 20,12 V20 Q20,22 18,22 H6 Q4,22 4,20 V12 Q4,10 6,10 Z M12,15 V18 ";
    internal const string Closed=Body+"M8,10 V6 C8,3.8 9.8,2 12,2 C14.2,2 16,3.8 16,6 V10";
    internal const string Open=Body+"M8,10 V6 C8,3.8 9.8,2 12,2 C14.2,2 16,3.8 16,6";
}
