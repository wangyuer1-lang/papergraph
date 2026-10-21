# Windows 0.16.0-preview.8

This Windows build adds direct Full text editing with the same source-boundary buffer used by Mac.
Build and maintain it in `wangyuer1-lang/papergraph`.

## Writing and interaction

- Citations and their parentheses automatically use a lemon background distinct from the five text-color choices; dark mode uses a muted lemon fill. Mac and Windows use the same highlight ranges. This display formatting preserves source font colors, text, copy output, and document history.

- Click directly in Full text to edit a body or parenthetical reference. Changes update the source proposition and inspector, automatically save, and use graph Undo/Redo with Ctrl+Z/Ctrl+Y. Repeated citations update together while preserving the active selection and page. Generated brackets, separators and replacements crossing source boundaries are protected. Context menus support plain-text paste. English, Chinese and Japanese editing guidance is included.

- Proposition, ring and frame bodies support selected text colors and clearing color. Full text renders the same saved ranges, including repeated parenthetical citations. Single-headed Reference arrows point toward the body proposition; multiple cited sources share one pair of parentheses. The editor uses native WPF text selection and input; source Unicode, existing line endings and the shared UTF-16 range format remain intact.
- Text and colors undo/redo together. Graph copies, saves, agent operations and LAN synchronization retain the shared model. The inline AI protection lock prevents agent changes while allowing human editing.
- The canvas provides minus/plus zoom, percentage reset and Actions. H enables persistent pan, V selects, and holding Space temporarily pans. Shift+Space toggles titles. A frame border can be moved with the left mouse button; its lower-right corner resizes it. Existing right-button operations remain available.
- English, Chinese and Japanese labels and help cover the Windows controls. Windows uses Ctrl shortcuts and native Windows dialogs; Mac retains Command shortcuts and native trackpad gestures.

## Synchronization

The Windows TLS host now imports its generated certificate into a temporary current-user key container compatible with Schannel. The container is not marked persistent; it is released with the certificate. The in-memory PFX buffer is zeroed after import. Certificate pinning, token validation, expiration, both-device approval and conflict backups are unchanged.

LAN sync supports Mac and Windows. The Mac Bluetooth adapter is experimental and needs a separately distributed compatible Android app; no Android app is published in this release. Windows Bluetooth radio support is not implemented.

## Validation

Run these against isolated test data, never the production library:

```powershell
dotnet build Papergraph.csproj -c Release -o artifacts/build
$test = Start-Process ./artifacts/build/papergraph.exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($test.ExitCode -ne 0) { Get-Content artifacts/build/test-failure.txt; throw 'Desktop tests failed' }
$sync = Start-Process ./artifacts/build/papergraph.exe -ArgumentList '--lan-sync-test' -WindowStyle Hidden -Wait -PassThru
if ($sync.ExitCode -ne 0) { Get-Content artifacts/build/test-failure.txt; throw 'Sync tests failed' }
```

The native Full text editing suite also checks source/inspector updates, native paragraph breaks, Unicode and colors, protected separators, empty bodies, repeated citations and caret shifts, undo/redo, stale source rejection, page/graph changes, composition-event deferral, automatic saving, reopening and translated help. Screenshots cover light and dark modes. Composition-event tests do not substitute for real IME candidate-window testing.

The Windows parity suite checks native paragraph structure, exact Unicode/line endings, selected text ranges, colors in both editors, clear/undo/redo, human editing under AI protection, rejected agent retries, persistence and light/dark rendering. Existing desktop suites exercise graph movement, selection, layout, notes, library/categories, live full text and the agent interface. Sync tests exercise actual Windows loopback TLS and simulated fragmented Bluetooth streams, including certificate/token rejection, confirmation, merging and crash recovery.

A Mac project build on Windows is a compilation check, not a real macOS runtime test. Physical Windows–Mac LAN/firewall behavior and interactive Chinese/Japanese IME composition require hands-on testing. Simulated transport tests do not establish physical Bluetooth support.

## Upgrade

Close Papergraph after saving, keep a backup of the application and library, then replace the application files. Preserve `Data`, external graph paths and `.lan-sync` metadata. Release packages contain no personal graph files. Launch with `--data-dir` if the existing library is outside the application folder.
