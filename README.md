# papergraph

**Write papers as graphs. Inspect the parts. Collaborate with agents.**

papergraph is a local Windows desktop tool for writing papers through propositions and their relationships. It makes the parts of a paper visible and individually editable, so authors can inspect claims, record evidence, examine connections and work with an AI agent on specific pieces of the argument.

AI-generated prose can sound coherent while leaving assumptions, unsupported claims and weak connections difficult to locate. papergraph makes that review easier: a proposition, its supporting notes and its relationships can be examined directly, then revised as part of a larger structure.

[Download for Windows](https://github.com/wangyuer1-lang/papergraph/releases/latest) · [User guide](docs/usage.md) · [Agent interface](Agent%20guide.md) · [中文介绍](README.zh-CN.md) · [Sponsor](https://github.com/sponsors/wangyuer1-lang)

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

1. Download the Windows x64 ZIP from [Releases](https://github.com/wangyuer1-lang/papergraph/releases/latest), extract it into a writable folder, then open `papergraph.exe`. The current downloadable app build is **0.14.3** and includes its .NET runtime.
2. Click **+** beside **Graphs** to create a graph, then enter its title in the top bar.
3. Double-click the canvas to add a proposition. Select it to edit its body and notes.
4. Drag its connection handle to another point or ring. Select the arrow to edit its meaning, direction and notes.
5. Right-drag to select objects, then use the bottom actions to group or delete. Press **F** or click **Fit all** to see the whole current board. Use **Fit edit / Shift+F** to retain the original 10% minimum zoom for editing. Press **Space** to toggle titles.

Use the top-right **Links** button to cycle between **All**, **Within** (shared innermost □) and **Across** (different innermost frames, or a frame to outside). A common outer frame does not turn cross-frame references into internal connections. This changes only visibility and remembers your preference.

Right-click a frame or selection to **Copy**, then right-click the destination to **Paste**; Ctrl+C / Ctrl+V also work on the canvas, including across pages. **Arrange inside** follows directed connections within a frame while retaining ring sizes and frame membership. Mark points, rings or connections with a review color to find items needing attention.

Right-click a graph title to rename it, save it to another folder, show its file in Explorer or move it to the Windows Recycle Bin.

## Interface languages

Choose **View → Language** for English, 简体中文 or 日本語. The interface switches immediately and remembers your choice. Your writing and graph files keep their original content. See [translation resources](docs/localization.md) for coverage and how to add a language.

## New in 0.14.3

- **Live full text:** read the manuscript below the graph as you edit. Directional Body arrows determine the sequence; rings introduce line breaks and frames separate paragraphs. Disconnected manuscripts have separate pages. Reference arrows and isolated points stay out of the body, and checks identify ambiguous ordering and cycles. Click text to locate its source proposition.
- **Words and characters:** live counts for the current text page and selected text, including Chinese, Japanese and mixed-language writing. Notes and excluded points are not counted.
- **Four Notes pages:** each object has tabs `1 2 3 4`. The first page is reserved for your writing; agents write to pages 2–4. Notes remain separate from manuscript text.
- **Frames and categories:** organize graphs in sidebar categories, connect frames directly and keep frame titles above their borders. Double-tap an arrow key to move into a nearby frame. Internal boards retain a faint frame boundary; grips scale with zoom, and adding a point keeps the camera in place.
- **Agent interface:** read the ordered manuscript with `fullText`, distinguish Body and Reference relations, specify branch order, manage categories and edit the permitted Notes pages through the existing revision-checked interface.

See the [user guide](docs/usage.md) for reading-order rules, navigation and counting details.

## Working with an agent

The local agent interface can read the live document (including unsaved writing, IDs, relations, group membership and selection) and search its text. `addNodes` adds independent propositions. `editGraph` can create or update points, relations and rectangular regions, position objects, and create connected circular groups in the running app.

`fullText` reads manuscript pages, source point IDs and ordering checks without operating the desktop window. `snapshot`, `search` and `fullText` also support inactive library pages. Notes page 1 is reserved for the author; ordinary agent writes use pages 2–4. Categories can be created, renamed and assigned through the same interface.

Writes require the expected document, revision and a stable request UUID. Each accepted batch saves before reporting success and can be undone in one step. Stale writes are rejected so an agent cannot silently overwrite newer edits. The interface uses a Windows named pipe restricted to the current user. See the [request formats and CLI examples](Agent%20guide.md).

Agents can also use `listGraphs`, `createGraph`, `duplicateGraph` and `openGraph` to manage separate pages. `snapshot` and `search` can read another page without switching. New pages and complete copies open in the background by default, preserving the user's current view; copying retains all writing, notes, frames, rings, positions, colors and connections. Pending edits save before changing pages, and source/target revision checks prevent stale operations. Creation receipts survive restarts so retries do not create duplicate pages or overwrite later edits. Page creation and switching are separate from canvas undo.

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

Self-tests use isolated temporary documents and cover persistence, graph geometry, selection, grouping, camera controls, editor themes, live full text and counts, Notes pages, frame navigation, agent operations and library/category management.

Create a portable Windows x64 build:

```powershell
dotnet publish Papergraph.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish
```

## Support papergraph

If papergraph helps you write or review a paper, consider [sponsoring my work](https://github.com/sponsors/wangyuer1-lang). Your support helps me keep maintaining the project, improving the writing workflow and documenting how to use it. Any amount is welcome. Thank you for your support!

## Contributing

Issues and pull requests are welcome. Use synthetic examples in bug reports and exclude private manuscripts, credentials and local library files. Describe the writing problem being addressed and run the self-tests on Windows. Keep graph interaction simple and preserve existing document content.

## License and acknowledgements

[MIT](LICENSE). The graph layout implementation is written in C#; design references are listed in [Layout references](Layout%20references.md). Referenced JavaScript layout libraries are not bundled. The portable release includes Microsoft's .NET runtime and accompanying license notices.
