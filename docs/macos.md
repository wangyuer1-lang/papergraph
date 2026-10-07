# macOS preview

Papergraph 0.15.0-preview.7 adds an Avalonia desktop target in `Mac/`. It uses the existing graph model, file format, graph surface, layout algorithms, note rules and agent operations. The original Windows WPF target remains available.

## Download and install

Download [0.15.0-preview.7](https://github.com/wangyuer1-lang/papergraph/releases/tag/v0.15.0-preview.7). Use `macos-arm64` for Apple Silicon or `macos-x64` for Intel, extract the ZIP, and move `Papergraph.app` to Applications. Requires macOS 14 or newer; the .NET runtime is included.

This preview is ad-hoc signed and is not notarized by Apple. If macOS blocks it, review [Apple’s instructions for opening an app from an unidentified developer](https://support.apple.com/en-us/102445). Do not disable Gatekeeper globally. Intel builds have not yet been tested on Intel hardware.

## Run and build

Install the .NET 10 SDK. No WPF or Windows runtime is required for the Mac target.

```sh
dotnet run --project Mac/Papergraph.Mac.csproj
# Run tests in a disposable library, never a real manuscript directory:
dotnet run --project Mac/Papergraph.Mac.csproj -- --self-test --data-dir /tmp/papergraph-mac-tests
scripts/package-macos.sh arm64
scripts/package-macos.sh x64
```

The preview targets macOS 14 or newer, matching the .NET 10 minimum listed in [Microsoft’s macOS installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/macos).

The build script creates a self-contained `Papergraph.app` and ZIP. Recipients do not need .NET. The default build uses an ad-hoc signature for preview testing. Set `MACOS_SIGN_IDENTITY` to an installed Developer ID Application identity for a distribution signing pass. Notarization and stapling are separate release steps and require the maintainer's Apple developer credentials; the script does not claim to perform them. See [Apple's notarization documentation](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution).

The GitHub Actions macOS workflow runs the desktop regression suite on its native runner architecture and builds both `osx-arm64` and `osx-x64`. Building the Intel package does not constitute a native Intel interaction test.

## Using the preview

- Use File → Open to load `.papergraph`, legacy `.yujian`, or backup files.
- Double-click empty canvas to add a proposition. Drag its small connection handle to connect it to another point or frame.
- Select (V): primary-drag empty canvas to box-select; drag the selected objects to move them. Shift-click or Command-click extends the selection. Drag a frame border to move it, and its selected lower-right handle to resize it. Actions → Move □ and contents toggles whether its points move with it.
- Pan (H): primary-drag anywhere to pan without changing the selection. Hold Space for temporary pan and release it to restore the current tool. Two-finger scrolling pans in both axes.
- Pinch to zoom around the pointer; Command-scroll also zooms. The visible − / + buttons zoom around the canvas centre, and clicking the percentage resets to 100%.
- Actions exposes commands for the selection or canvas; the permanent ··· beside Graphs exposes graph/category commands. Secondary click and Control-click remain optional shortcuts. Existing right-drag gestures are retained.
- F or Command-0 fits the graph; Shift+F fits for editing; Shift+Space toggles captions; Z switches to detail view. Delete (⌫) deletes selected canvas objects. Text fields retain normal typing and deletion.
- ⌘S saves. ⌘Z / ⌘⇧Z undo and redo graph actions when the canvas has focus. Native text fields retain their normal editing shortcuts.
- Double-click a ring or frame to enter it. The back button restores the preceding board and viewport.
- Notes have four pages. Page 1 remains reserved for human writing; agents write pages 2–4.
- Full text uses the same reading-order calculation and opens below the graph, with a draggable divider, numbered pages, reading-order checks and selection-aware word/character counts. It is a read-only manuscript, as on Windows. Click a passage to select its source point and edit the source in the right panel. Copy exports the current manuscript page.
- The language selector offers English, Simplified Chinese and Japanese. Some preview diagnostics remain English.

The default library is `~/Library/Application Support/Papergraph`, outside the signed app bundle. `--data-dir` selects a different library. Copy Windows graph files to your Mac and open them through the app; Windows absolute library paths need to be reselected on macOS. Autosave and `.bak` backups use the shared storage implementation. A failed save keeps the editor open. The app prevents two processes from opening the same library.

## Agent interface

The existing [Agent guide](../Agent%20guide.md) request/response schemas remain in use. Run the bundled executable instead of `papergraph.exe`:

```sh
'/Applications/Papergraph.app/Contents/MacOS/papergraph' \
  --agent-request /tmp/request.json --agent-response /tmp/response.json
```

Supply the same `--data-dir` to the GUI and client if using a custom library. The current-user-only .NET named-pipe transport is tested on macOS. Path identity preserves case on Unix systems so differently cased directories do not share an IPC name. The app retains revision checks, durable-before-acknowledgement writes, request IDs and personal-note protection.

## Architecture and review scope

`Mac/Compatibility` is a small adapter for the shared custom graph canvas: mutable geometry, draw operations and pointer events are mapped to Avalonia. It is not a general WPF implementation. Linking the existing graph sources avoids maintaining divergent graph algorithms or silently changing the document schema. The surrounding controls, native text editing, file pickers and clipboard use Avalonia directly. Trash uses Foundation's `trashItemAtURL` operation.

Preview 2 follows the Windows window structure: a 46-pixel toolbar, 218-pixel category tree, graph workspace, and 320-pixel inspector with a two-thirds proposition / one-third Notes split. The palette, glyphs, relation buttons, four Notes tabs, selection actions, menus, context actions and bottom manuscript panel follow the original. Command replaces Control for Mac shortcuts; Command-N adds a proposition, and Escape and double-tapped arrow keys follow the original navigation. Preview 4 assigns the Mac Delete (⌫) key to object deletion; use Escape or the visible back button to leave a board. Category drag/drop, per-object note-page selection and board return state are retained.

Preview 3 renders menus in the window overlay, tracks context-action positions on press/release, and supports Mac Control-click. Independent button and menu themes prevent Fluent pressed/focus styling from leaking through. Sun, moon, plus, category, back, link and more symbols use fixed vector silhouettes and centred sizing rather than platform font fallback.

Preview 4 adds a compact canvas toolbar with Select, Pan, zoom and Actions controls, a context-sensitive gesture hint and a persistent library action button. Ordinary dragging selects/moves objects, scrolling pans, and native touchpad magnification zooms. Command shortcuts, temporary Space panning and Mac Delete respect text-editor focus. English, Simplified Chinese and Japanese help describe these controls.

Preview 5 rebuilds the Mac application icon directly from the Windows `app.ico`, embeds both formats, and assigns the native `NSApplication` icon explicitly so it also appears when launched outside Finder. The theme symbols now follow the original U+2600 / U+263E font artwork and 21-pixel em size: a filled sun with eight rays and an outlined crescent. This replaces the earlier approximate drawings. The button tooltip describes the destination theme, matching Windows. No font file is bundled. The comparison uses original Windows XAML and a local Segoe UI Symbol reference; it is not a screenshot comparison against a running Windows installation.

The subsequent icon unification also updates the Windows target: both projects now compile the same root `ThemeSymbols.cs`, including the vector paths, 21-pixel size and centring calculation. WPF's `ThemeGlyph` and Avalonia's `ToolbarGlyph` draw that shared artwork with the current muted foreground colour. Windows no longer uses font characters for the theme button, so font fallback cannot substitute a different sun or moon. The shared application icon remains `app.ico`.

Native window chrome, font rasterization, file dialogs and IME are provided by macOS/Avalonia. Pixel-for-pixel equivalence on a Windows machine has not been measured. The manuscript uses an Avalonia selectable text layout rather than WPF FlowDocument, so paragraph typography may differ. Finder document associations, an automatic updater and canvas-object VoiceOver support still require separate work. Pinch handling is connected to Avalonia’s native macOS magnification event and covered by routed-event tests; a physical trackpad pinch still needs hands-on verification.

## Validation

The Mac self-tests reuse the existing model, spacing, caption layout, ring connection and agent library regression suites. Additional checks exercise Unicode persistence, native editor changes, note-page isolation, undo/redo, copy/paste ID remapping, full-text source editing, stale request rejection, idempotent retries, durable writes, categories, Unix path identity, the live agent pipe and rendering. The parity suite also checks menu structure, panel sizing, per-object note tabs, the six relation markers, duplicate-link rejection, note isolation, board return selection/view, Command-N, Escape, frame selection bounds, double-tap navigation, manuscript source location, and the library selection after autosave. Light, dark and relation-editor screenshots are saved with the test evidence.

The pointer suite drives routed press/move/release events through the compatibility adapter: point and canvas menus, Control-click, menu Escape/focus, selection dragging, frame movement/resizing, capture cancellation, popup placement, icon centring, and light/dark pressed feedback. Rendered evidence is saved outside the app bundle. Actual macOS menu clicks are also checked manually in an isolated library.

The Mac interaction suite additionally checks primary-button marquee/group/frame movement and resizing, undo, Command-click selection, Pan over objects without selection loss, hold-Space and text-focus isolation, two-axis wheel panning, pointer-anchored Command-scroll and magnification, Mac Delete, visible canvas/library commands, and controls wrapping at the minimum window width.

The icon suite renders both theme symbols and complete theme buttons, checks two click transitions and their destination tooltips, and exports the expected and native application icon images. AppKit copies and color-converts icon images, so rendered shape comparison is used instead of pointer or encoded TIFF byte equality. The 64-pixel ICNS representation is also checked against the original ICO pixels. Native Apple Silicon release-bundle tests and both architecture builds passed for preview 5; native Intel execution remains unverified.

Before a public stable release, test real manuscripts and large graphs on both Apple Silicon and Intel Macs, test Chinese and Japanese IME composition interactively, run the Windows self-tests on Windows CI, and sign/notarize the release with the maintainer's identity. Never run self-tests against the user's production data directory.

Preview 6 gives the shared application mark a white rounded tile and soft shadow. `scripts/build-app-icon.py` generates `Assets/AppIcon.png` and Windows `app.ico`; normal builds consume these checked-in files. macOS icon generation reads the PNG master directly, avoiding the paletted-ICO alpha decoding issue in `sips`. Only the space outside the white tile is transparent.
