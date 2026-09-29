# papergraph

**Write papers as graphs. Inspect the parts. Collaborate with agents.**

papergraph is a local Windows desktop tool for writing papers through propositions and their relationships. It makes the parts of a paper visible and individually editable, so authors can inspect claims, record evidence, examine connections and work with an AI agent on specific pieces of the argument.

AI-generated prose can sound coherent while leaving assumptions, unsupported claims and weak connections difficult to locate. papergraph makes that review easier: a proposition, its supporting notes and its relationships can be examined directly, then revised as part of a larger structure.

[Download for Windows](https://github.com/wangyuer1-lang/papergraph/releases/latest) · [User guide](docs/usage.md) · [Agent interface](Agent%20guide.md) · [中文介绍](README.zh-CN.md)

![Illustrated workflow: write propositions, connect an argument, inspect evidence and collaborate with an agent](docs/quickstart.svg)

## A paper made of inspectable parts

| Object | Role in writing |
| --- | --- |
| Proposition point | A claim, question or passage, with a short title and separate notes for evidence and sources. Point size reflects proposition length, not notes. |
| Relation arrow | A connection with a direction, semantic symbol, optional title and its own notes. |
| Rectangular region | A spatial grouping for a topic or section. Regions can overlap and have their own writing. |
| Circular group | An open ring around a connected argument. Contents stay visible, and the ring itself can connect to other objects as a whole. |

Double-click a region or ring to focus on its internal board, then return to the overview. Light and dark themes are included.

## Start writing

1. Download and extract `papergraph-0.12.1-win-x64.zip` into a writable folder on Windows, then open `papergraph.exe`. The release includes its .NET runtime.
2. Click **+** beside **Graphs** to create a graph, then enter its title in the top bar.
3. Double-click the canvas to add a proposition. Select it to edit its body and notes.
4. Drag its connection handle to another point or ring. Select the arrow to edit its meaning, direction and notes.
5. Right-drag to select objects, then use the bottom actions to group or delete. Press **F** or click **Fit** to see the current board. Press **Space** to toggle titles.

Right-click a graph title to rename it, save it to another folder, show its file in Explorer or move it to the Windows Recycle Bin.

## Working with an agent

The current local agent interface can read the live document (including unsaved writing, IDs, relations, group membership and selection), search its text, and add independent propositions without adding relations or moving existing objects.

Writes require the expected document and revision. Batch additions save before reporting success and can be undone. The interface uses a Windows named pipe restricted to the current user. See the [request formats and CLI examples](Agent%20guide.md).

There is no built-in model, automatic fact checking or Zotero search. An external agent must verify sources and use the interface. Reading the graph through an external agent may send its content to that agent's provider, depending on your setup.

The longer-term goal is collaborative writing with clearer source tracking, proposed changes and human review. Those workflows are not yet implemented.

## Local files

Graphs are readable `.papergraph` JSON files. Autosave writes to `Data` beside the executable unless a different data directory or save location is chosen. Existing `.yujian` documents remain readable. A save keeps the preceding version as `.bak`; Markdown export is available. Undo history lasts for the current session.

Keep your `Data` folder when upgrading. Release archives contain no personal graphs or library state. To use a separate library:

```powershell
.\papergraph.exe --data-dir "D:\My Papers\Graphs"
```

## Build and test

Requires Windows and the **.NET 10 SDK** with desktop support. The application uses C# and WPF and currently targets Windows only.

```powershell
dotnet build Papergraph.csproj -c Release -o artifacts/build
$test = Start-Process ./artifacts/build/papergraph.exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($test.ExitCode -ne 0) { Get-Content ./artifacts/build/test-failure.txt; throw 'Self-tests failed' }
```

Self-tests use isolated temporary documents and cover persistence, graph geometry, selection, grouping, camera controls, editor themes, agent operations and library management.

Create a portable Windows x64 build:

```powershell
dotnet publish Papergraph.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish
```

## Contributing

Issues and pull requests are welcome. Use synthetic examples in bug reports and exclude private manuscripts, credentials and local library files. Describe the writing problem being addressed and run the self-tests on Windows. Keep graph interaction simple and preserve existing document content.

## License and acknowledgements

[MIT](LICENSE). The graph layout implementation is written in C#; design references are listed in [Layout references](Layout%20references.md). Referenced JavaScript layout libraries are not bundled. The portable release includes Microsoft's .NET runtime and accompanying license notices.
