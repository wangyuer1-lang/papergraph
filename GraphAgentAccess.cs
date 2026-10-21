using System.IO;
namespace Papergraph;

// Graph-level agent permission. Human editing remains available.
internal static class GraphAgentAccess
{
    internal static void RequireWritable(GraphDocument document)
    {
        if(document.AgentReadOnly)
            throw new InvalidDataException("This graph is AI read-only. Agents may read it but cannot modify it. Only the user may turn off AI read-only in the graph menu; do not bypass this protection through the UI or files.");
    }
}
