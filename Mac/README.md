# Papergraph for macOS

The macOS application uses Avalonia 11.3.22 and .NET 10, sharing Papergraph's graph model, file format and editing logic with the Windows application.

## Downloads and documentation

- [Download Papergraph for macOS](../README.md#downloads)
- [macOS user guide](../docs/macos.md)
- [Build and test instructions](../docs/macos.md#run-and-build)
- [Agent interface guide](../Agent%20guide.md)

## Implementation

`Compatibility/` adapts the shared custom graph canvas to Avalonia. Window and text controls use Avalonia directly. See the [architecture notes](../docs/macos.md#architecture-and-review-scope) for compatibility details and preview limitations.
