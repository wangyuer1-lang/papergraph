# Papergraph

**A visual workspace for research writing.**

[English](README.md) · [简体中文](README.zh-CN.md)

Papergraph is a local desktop application for developing papers through propositions and their relationships. Organize claims, record evidence and inspect the structure of an argument while keeping the writing and its supporting notes together.

Available for Windows, with a macOS preview. An optional local agent interface lets external AI agents read and edit specific parts of a document under revision checks.

## Downloads

| Platform | Architecture | Release | Download |
| --- | --- | --- | --- |
| Windows | x64 | Stable · v0.14.3 | [Windows stable](https://github.com/wangyuer1-lang/papergraph/releases/latest) |
| Windows | x64 | Preview · v0.15.0-preview.7 | [Windows preview](https://github.com/wangyuer1-lang/papergraph/releases/download/v0.15.0-preview.7/Papergraph-0.15.0-preview.7-windows-x64.zip) |
| macOS | Apple Silicon (M series) | Preview · v0.15.0-preview.7 | [macOS for Apple Silicon](https://github.com/wangyuer1-lang/papergraph/releases/download/v0.15.0-preview.7/Papergraph-0.15.0-preview.7-macos-arm64.zip) |
| macOS | Intel | Preview · v0.15.0-preview.7 | [macOS for Intel](https://github.com/wangyuer1-lang/papergraph/releases/download/v0.15.0-preview.7/Papergraph-0.15.0-preview.7-macos-x64.zip) |

**Windows:** extract the ZIP into a writable folder and open `papergraph.exe`. **macOS:** requires macOS 14 or newer; extract the ZIP and move `Papergraph.app` to Applications. All downloads include the .NET runtime.

The macOS preview is ad-hoc signed and has not been notarized by Apple. The Intel package has not been tested on Intel hardware. See the [macOS guide](docs/macos.md#download-and-install) for first-launch instructions and [release notes](https://github.com/wangyuer1-lang/papergraph/releases/tag/v0.15.0-preview.7) for preview details.

![Papergraph workflow: write propositions, connect an argument, inspect evidence and collaborate with an agent](docs/quickstart.svg)

## Core concepts

| Object | Purpose |
| --- | --- |
| Proposition | A claim, question or passage, with a short title and separate notes for evidence and sources. Point size reflects proposition length. |
| Relation | A directed connection with a semantic symbol, optional title and its own notes. |
| Frame | A rectangular region for organizing a topic or section. Frames can overlap, contain writing and connect to other objects. |
| Group | An open ring around a connected argument. Its contents stay visible, and the group can connect to other objects as a whole. |

Double-click a frame or group to enter its internal board, then return to the overview.

## Features

- **Graph editing:** connect propositions, organize frames and groups, copy selections across graphs, arrange connected content and mark objects with review colors.
- **Live manuscript:** view text below the graph as you write. Directed Body relations determine reading order; Reference relations and isolated points stay out of the manuscript. Ordering checks identify ambiguity and cycles. Click a passage to locate its source proposition.
- **Word and character counts:** track the current manuscript page or selected text, including Chinese, Japanese and mixed-language writing.
- **Four Notes pages:** keep supporting material separate from manuscript text. Page 1 is reserved for the author; agents write to pages 2–4.
- **Library organization:** manage graphs in sidebar categories, with autosave, backup files and Markdown export.
- **Interface preferences:** choose light or dark mode and English, Simplified Chinese or Japanese through **View → Language**. Language changes preserve document content; see [localization details](docs/localization.md).

## Getting started

1. Click **+** beside **Graphs** to create a graph and enter its title in the top bar.
2. Double-click empty canvas to add a proposition. Select it to edit its text and notes in the right panel.
3. Drag its connection handle to another object. Select the relation to edit its meaning, direction and notes.
4. Organize related propositions into frames or groups. Use **Fit all / F** to see the current board.
5. Open **Full text** to review the manuscript and follow passages back to their source propositions.

| Action | Windows | macOS |
| --- | --- | --- |
| Select an area | Right-drag empty canvas | Drag empty canvas with **Select (V)** |
| Pan | Left-drag empty canvas | Two-finger scroll or **Pan (H)** |
| Zoom | Mouse wheel | Pinch, Command-scroll or the **− / +** buttons |
| Copy / paste | Ctrl+C / Ctrl+V | Command+C / Command+V |
| More actions | Right-click | **Actions**, **···**, secondary click or Control-click |

See the [Windows user guide](docs/usage.md) or [macOS user guide](docs/macos.md#using-the-preview) for full controls and reading-order rules.

## Agent integration

The local interface lets an external agent read live documents, including unsaved writing, and work with specific objects. `snapshot`, `search` and `fullText` can also read inactive library pages without switching the user's view.

`addNodes` and `editGraph` support proposition, relation, frame and group edits. Other operations manage pages and categories. New graphs and complete copies open in the background by default; copying preserves content, notes, positions, colors and connections.

Writes require the expected document, a fresh revision and a stable request UUID. Accepted graph-edit batches save before reporting success and can be undone in one step. Stale writes are rejected, and durable creation receipts prevent duplicate pages on retry. Notes page 1 remains reserved for the author. Page creation and switching are separate from canvas undo.

The interface uses a local named pipe restricted to the current user on Windows and macOS. See the [agent interface guide](Agent%20guide.md) for request formats and CLI examples.

Papergraph has no built-in AI model, automatic fact checking or Zotero search. External agents must verify sources; depending on the agent's configuration, document content may be sent to its provider. Source tracking, proposed changes and human review are planned workflows.

## Data storage

Graphs are readable `.papergraph` JSON files; existing `.yujian` documents remain supported. Each save retains the preceding version as `.bak`. Markdown export is available, and undo history lasts for the current session.

| Platform | Default library |
| --- | --- |
| Windows | `Data` beside `papergraph.exe` |
| macOS | `~/Library/Application Support/Papergraph` |

Preserve your library when upgrading, especially the Windows `Data` folder. Release archives contain no personal graphs or library state. A custom library can be selected with `--data-dir`, for example:

```powershell
.\papergraph.exe --data-dir "D:\My Papers\Graphs"
```

## Documentation

| Guide | Contents |
| --- | --- |
| [Windows user guide](docs/usage.md) | Writing, navigation, relations and manuscript reading order |
| [macOS user guide](docs/macos.md) | Installation, Mac controls, compatibility and build instructions |
| [Agent interface guide](Agent%20guide.md) | Live document access, revision checks and automation |
| [Localization guide](docs/localization.md) | Interface language coverage and translation resources |
| [Release notes](https://github.com/wangyuer1-lang/papergraph/releases) | Published versions and download history |

## Build and test

The Windows application uses C# and WPF and requires Windows with the **.NET 10 SDK** for desktop execution. The macOS application uses Avalonia and shares the graph sources; see the [macOS build instructions](docs/macos.md#run-and-build).

Build and run the Windows self-tests:

```powershell
dotnet build Papergraph.csproj -c Release -o artifacts/build
$testData = Join-Path $env:TEMP ("papergraph-tests-" + [guid]::NewGuid().ToString("N"))
$test = Start-Process ./artifacts/build/papergraph.exe -ArgumentList "--self-test --data-dir `"$testData`"" -WindowStyle Hidden -Wait -PassThru
if ($test.ExitCode -ne 0) { Get-Content ./artifacts/build/test-failure.txt; throw 'Self-tests failed' }
```

Self-tests cover persistence, graph geometry, selection, grouping, camera controls, themes, manuscript order and counts, Notes pages, navigation and agent operations. Always use an isolated test library.

Create a portable Windows x64 build:

```powershell
dotnet publish Papergraph.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish
```

## Contributing

Issues and pull requests are welcome. Describe the writing problem being addressed, use synthetic examples in bug reports and exclude private manuscripts or credentials. Run the relevant platform self-tests, keep graph interaction simple and preserve existing document content.

## Support

If Papergraph is useful to your work, [sponsor the project](https://github.com/sponsors/wangyuer1-lang) to support maintenance, writing-workflow improvements and documentation.

## License and acknowledgements

Papergraph is released under the [MIT License](LICENSE). The graph layout implementation is written in C#; design references are listed in [Layout references](Layout%20references.md). Referenced JavaScript layout libraries are not bundled. Portable releases include Microsoft's .NET runtime and its accompanying license notices.
