# papergraph

A native Windows graph workspace for building an argument and writing a paper. Open `papergraph.exe`; no browser or account is needed.

## Writing

Select a point, ◎ or □. The fixed right panel has two independent fields: the upper two thirds are the **Proposition**, and the lower third is **Notes**. Both save automatically. Only the proposition affects point size; notes never enlarge a point. A ◎ surrounds its graph and sizes itself from the contents. Each field scrolls independently.

Select a relation to edit its own **Notes** below the arrow settings. These notes save automatically, support undo and redo, and remain attached when you change the relation's symbol or direction. Markdown exports include them beneath the corresponding relation.

Points, ◎ and relations also have an optional **Title** at the top of the right panel. Use a word or short phrase to summarize the object on the graph. Point titles choose nearby open positions; relation titles choose suitable positions along either side of their lines. Placement avoids arrow shafts, arrowheads, points and other titles where space allows. Chosen positions stay attached during pan, zoom and dragging. Graph edits only relocate a title when an obstruction is substantial and a clearly better position exists. Titles start hidden. Press **Space** on the graph to show all titles; press it again to hide them. Holding Space toggles only once, and typing spaces in an editor works normally. Titles stay at least 16 screen pixels when zooming out and grow with the graph when zooming in. They do not automatically disappear when crowded or zoomed out. Text crossing the canvas edge is clipped normally. Click a visible title to select its object. Titles never change point size, and remain independent from propositions, notes and arrow symbols. They save automatically and appear in Markdown exports. Older papers open with empty titles.

Ordinary points have a clear size progression from short claims to long paragraphs, then grow gently toward a cap. At normal zoom, 20, 100 and 400 proposition characters give diameters of about 28, 42 and 56 pixels; empty points stay at 24 pixels and the maximum is 64. Size differences remain visible when zooming out. Only proposition text counts for ordinary points. ◎ are open rings around connected graphs; their internal points and relations remain visible. Notes, connections and internal graphs survive grouping and ungrouping. If an ungrouped ◎ has its own text or direct connections, it remains as an ordinary point.

## Navigating and selecting

- Left-drag empty canvas to pan. The wheel zooms around the pointer, including over the bottom action bar. Inside the writing panel, the wheel scrolls text.
- Right-drag to select points and fully enclosed □. Drag any selected object or the empty area inside the selection to move the selection. Shared points move only once.
- The bottom bar contains **□**, **◎** and **Delete**. ◎ requires at least two connected points. Selected □ can join that internal board.
- Ctrl / Shift click adds or removes a point or □. Ctrl / Shift right-drag adds another area. Ctrl+A selects all visible points and □. Esc or a click outside clears the selection.
- Right-click a □ or a selected area and choose **Copy**, then right-click a destination and choose **Paste**. **Ctrl+C / Ctrl+V** work on the canvas too; keyboard paste uses the pointer position, or the view centre when the pointer is outside the canvas. A copied □ includes its points, nested □ and ◎, internal connections, titles, propositions, notes, colors and relative positions. Paste works across Graphs pages and open papergraph instances, creates independent IDs and is undone in one step. Links to objects outside the copied selection are omitted. A frame board keeps the pasted fragment inside its bounds; if it is too small, enlarge it or paste in the overview. Text editors keep normal text copy/paste. Finishing a right-drag selection still shows the three bottom actions; right-click the selection again to open its copy menu.
- Delete removes selected objects. Deleting a □ alone keeps its unselected points. Undo restores a mixed deletion in one step.
- **Fit all / F** shows every object on the current board, including frames and expanded rings. The wheel can zoom out below the former 10% limit. **Fit edit / Shift+F** fits the same board with a 10% minimum for editing. Z focuses a selection or pointer location; press it again to restore the overview.
- Arrow keys select a nearby point in that direction and show its writing in the right panel. With no selection, the first arrow selects the point nearest the center of the view. Off-screen selections come into view automatically. Navigation stays inside the current board and stops at its edges. While editing text, arrows move the caret normally; Esc returns focus to the graph, and Enter opens the selected point's writing.

## □ and internal boards

- R draws an absolute □. The bottom □ action uses the selection rectangle.
- A single left-click selects a □. Right-drag its border or top grip to move it. Right-drag its lower-right handle to resize it.
- □ movement defaults to the □ alone. Hold Shift during a right-drag to move its contents, or choose that mode from its context menu.
- New □ get different colors. Points inherit their containing □'s color; intersections have their own color. Overlapping □ remain independently movable.
- To flag a point, ring or connection for review, select it and click the color dot at the top of its card (or right-click → Mark color). Choose Red, Orange, Yellow, Green, Blue or Purple. Marks override frame colors, remain visible in both themes, save locally and support undo. **Automatic** removes the mark and restores the current automatic color; **Custom…** accepts a hex color.
- ◎ requires a connected selection from one parent group. Its ring automatically encloses its members, including nested groups. Click the ring border to edit its proposition and notes; drag the border to move all members together. Click or drag a point inside to work on that point alone. Right-click the ring to ungroup without moving its contents. Existing collapsed groups open automatically with their writing and connections preserved.
- Double-click a □ or ◎ to enter its board. Back / Esc restores the previous board and view. Space toggles titles without opening or closing an internal board.

## Relations and layout

The top-right **Links** button cycles through **All → Within → Across**; the View → Connections menu also selects a mode directly. Within shows links whose endpoints share an innermost □, including points inside its rings. Across shows links between different innermost frames or from a frame to outside. A large outer frame does not make links between separate inner frames count as Within. Overlapping frames retain shared membership; identical frames remain equivalent. If neither endpoint belongs to a □, its link appears in All only. Hidden links and their titles cannot be clicked. This display preference survives restarting and changes no connections, positions, layout constraints or saved graph content.

Drag the small connection handle beside a point to another point. Click a relation to choose a direction and symbol: solid triangle for Support, bar for Opposition, double arrow for Inference, diamond for Qualification, hollow triangle for Definition, and circle for Association.

Dragging moves only the selected point or selected group members. Releasing a drag keeps those exact positions and never runs automatic layout or pulls neighboring points closer. Creating or connecting points applies a local layout adjustment in one step. Connected points retain a minimum separation. Single relations stay straight at every zoom; parallel relations use shallow lanes. Arrow symbols adapt to the available gap instead of bending short lines into loops. At extreme zoom, a symbol without enough room is omitted while its line remains. Typing in Notes does not affect geometry.

**Arrange** in the top toolbar (also View → **Arrange inside**) arranges the contents of the selected □ or ◎. Inside a board, it arranges that board; in the overview with no container selected, it works within each □ separately and leaves unframed objects in place. Frames stay fixed, including nested and overlapping frames. Rings retain their visible diameter and move as whole units within a frame; points never change their frame or ring membership. Cross-frame references do not pull the layout together. All single-headed arrow symbols follow their actual direction, including reversed arrows. Branches use directed layers; long chains can wrap into reading rows to suit the frame's width and height. The layout compares crossings and lines passing through other points, spreads the contents across the available space, and reserves border margins and readable arrow gaps. It does not zoom the canvas or compact rings to a fixed small radius. Space is reserved for titles at normal editing scale. If a bounded area cannot accommodate the arrangement, it is left in place and a message asks for more room. Repeating Arrange is stable; Ctrl+Z reverses the complete operation. Editing, dragging or changing boards cancels a pending arrangement. Arrange changes positions only: propositions, notes, marks and connections are preserved. It follows existing connections rather than inferring the argument from the text.

## Files

Agents can read the live graph, search its text and add independent propositions through a local interface. Batch additions are saved, protected by a content revision check and reversible with Undo. See **Agent guide.md** for JSON requests and command-line usage. No extra controls are added to the writing interface.

File provides New paper, Open, Save, Save a copy and Export Markdown. Ctrl+S saves, Ctrl+Z undoes and Ctrl+Y redoes. Undo history lasts for the current session.

Documents are readable JSON in `Data`. New documents use `.papergraph`; existing `.yujian` documents and `.bak` backups remain supported. Each save keeps the previous revision as `.bak`. Opening a backup restores it into a new file. User text and custom relation names are preserved as written; legacy built-in relation names appear in English.

Older versions that combined proposition and notes into one field keep that combined text in Proposition. You can move any part of it into Notes. Existing separate notes remain separate; the app does not guess how to split your writing.

Other computers need .NET 10 Desktop Runtime. The `Source` directory contains the C# WPF source. Build with the .NET 10 SDK: `dotnet build Papergraph.csproj -c Release`.


The enclosing circle is also a relation endpoint. Drag its right-side connector to a point or another circle, or drop a connector near any part of its boundary. The circle highlights and the preview snaps to its edge. These connections belong to the circle itself and do not rearrange its contents.
Use **Fit all** in the top-right toolbar (or press **F** on the canvas) for the whole board. **Fit edit / Shift+F** uses the original 10% zoom floor. Both change only the camera, preserving object positions.
The left Graphs sidebar lists your saved graphs. Use + beside Graphs to create an empty graph and type its title in the top bar. Click a title to switch; pending changes save first. Double-click the current row or press F2 in the list to edit its title. The list and last open graph persist across restarts.
Right-click a graph title for Rename, Save as…, Show in folder and Delete graph. Delete (also the Del key while the list is focused) sends the document to the Windows Recycle Bin. Save as lets you choose a local folder; the original is retained and the saved document stays in the list. By default, graphs are automatically stored as .papergraph files in Data beside the application.

## Agent page management (0.12.12)

The local agent interface can list Graphs pages, read or search another page without opening it, create a named empty page, copy an entire page, and switch pages. Background creation is the default, so the page you are editing stays open. Copying preserves writing, notes, rings, frames, positions, colors and connections. Pending changes save before a page operation, revisions prevent stale edits, and retrying a completed create/copy does not create another copy. See Agent guide.md for the JSON operations and examples.
