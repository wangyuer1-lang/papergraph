using System;
using System.IO;
namespace Papergraph;

// Signed macOS bundles must remain read-only, including when self-tests run.
internal static class TestEvidence
{
    internal static string OutputDirectory { get; set; } = AppContext.BaseDirectory;
    internal static string ResultsPath => Path.Combine(OutputDirectory, "test-results.txt");
}
