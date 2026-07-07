# [EventLogExpert](Home.md)

## Viewing Events

The main view has three regions: the **tab strip** (one tab per open log, plus a `Combined` tab when more than one log is open), the **event table**, and the **Details pane** (collapsible, bottom). The status bar runs along the very bottom — see [Opening Logs](Opening-Logs.md#live-log-behavior) for what it shows for live logs.

<!-- screenshot: main-view -->
![Main view: tabs, event table, Details pane](.images/EventLogExpert-CombinedView.png)

### Tab strip

- One tab per open log. The tab label is the file name without extension for `.evtx` logs and `{LogName} - {ComputerName}` for live channels. The tab tooltip shows the full path / channel name. Tabs that finished loading with zero rows are prefixed `(Empty)`; tabs still loading show a spinner next to the label.
- A `Combined` tab appears when two or more logs are open. It shows every event from every open log interleaved by time and rendered in the configured time zone.
- Click a tab to switch to it. The `×` on each per-log tab closes just that log; `Combined` has no close button and disappears on its own when the open-log count drops below two.

### Event table

The table is virtualized — only the rows currently in view are rendered, so loading large `.evtx` files stays responsive.

See [Performance](Performance.md) for how the virtualized table, the segmented sorted store, and streaming resolution keep very large logs fast to open and scroll.

**Configurable columns.** Right-click a column header to open the column menu:

- A toggle per column (checked = visible). The available columns are `Level`, `Date and Time`, `Activity ID`, `Log`, `Computer Name`, `Source`, `Event ID`, `Task Category`, `Keywords`, `Process ID`, `Thread ID`, and `User`. The `Description` column is fixed and always rightmost.
- An `Order By` submenu pinned to the same set of columns. The checked column is the current sort key.
- `Reset Column Defaults` returns the visibility, ordering, and sort to first-launch state.

The current sort indicator (a caret) appears in the active column header; clicking it flips between ascending and descending.

**Column reordering.** Drag a column header sideways to drop it before or after another column. The new order persists across sessions until `Reset Column Defaults` (in the column menu) restores it.

**Column sizing.** Drag a column-header edge to resize. Sizes persist across sessions; `Reset Column Defaults` restores the built-in widths along with visibility, ordering, and sort.

**Per-row highlighting.** When a filter has a `Highlight Color` set, every event matching that filter is rendered with that background color. The configured colors live alongside the filter — see [Filtering](Filtering.md). When several enabled, non-excluded filters could highlight the same row, the first one in pane order wins.

**Selection.** Click to select. Ctrl+Click toggles individual rows. Shift+Click selects a range from the anchor to the clicked row. Ctrl+Shift+Click extends the selection additively from the anchor to the clicked row without dropping rows you've already selected elsewhere. Arrow keys, Page Up / Page Down, Home, and End move within the table; Shift + those keys extends the selection. `Ctrl+A` selects every event in the table (including events hidden inside collapsed groups — see [Grouping](#grouping)); `Escape` clears the selection. The selection drives both the `Ctrl+C` clipboard copy (see [Keyboard and Copy](Keyboard-And-Copy.md)) and the Details pane.

**Right-click on a row.** Opens a context menu:

- `Copy Selected` / `Copy Selected (Simple)` / `Copy Selected (XML)` / `Copy Selected (Full)` — same four formats as the `Edit` menu.
- `Exclude Events Before` / `Exclude Events After` — sets a date filter using the right-clicked event's timestamp as the boundary.
- `Include` and `Exclude` submenus — each lists the field comparisons applicable to a single right-clicked event. Picking one creates a new basic filter (or exclusion) for that field equal to the right-clicked event's value. `Description` and `Xml` are not present in the submenu. Today this works for `Event ID`, `Activity ID`, `Level`, `Keywords`, `Source`, and `Task Category`; the `Process ID`, `Thread ID`, and `User ID` items are present in the menu but produce empty filter values, so they currently no-op.

### Grouping

Group the table by any column except `Description` so related events fold under a shared header row. Grouping is most useful for an identifier such as `Activity ID`, but works for every column in the `Group By` submenu.

**Turning grouping on.** Right-click a column header and pick a column from the `Group By` submenu; `(none)` turns grouping off. Groups are ordered by the grouped value, and events within each group keep the current `Order By` sort. Each header row shows the column name, the group value (or `(none)` when that value is empty), and the event count — for example `Activity ID: {guid} (42)`.

**Group direction.** `Group Descending` (on the group header's right-click menu) flips the order of the groups themselves between ascending and descending. It does not change the per-event `Order By` direction.

**Expanding and collapsing.**

- Click a group header, click its chevron, or press `Enter` while the header is focused to toggle that one group. `ArrowLeft` / `ArrowRight` also collapse / expand a focused header — see [Keyboard and Copy](Keyboard-And-Copy.md#grouped-event-table).
- `Expand All Groups` and `Collapse All Groups` are on both the group header's right-click menu and the `View` menu.
- Collapse state is **transient**: it is not persisted and resets whenever you switch to a different log tab.

**Selecting a group.** Right-click a group header and choose `Select Group` to select every event in that group, including events hidden by a collapse. `Ctrl+A` still selects every event in the table, also including those inside collapsed groups.

### Details pane

The Details pane sits at the bottom of the window. It hides itself when no event is selected. The header expand/collapse arrow toggles the pane between expanded and collapsed (the arrow's accessible name is `Details Expanded` / `Details Collapsed`); behavior on selection-change is governed by `Tools` → `Settings` → `Expand Display Pane On Selection Change`.

The top of the pane shows the same fields as a Windows Event Viewer details view: `Log Name`, `Source`, `Event Id`, `Level`, `Keywords` (only when present), and `Date and Time`. Below those, the `Description` paragraph is the resolved event description text (the same text the table previews in the `Description` column).

Below the description, an `XML` toggle (accessible name `XML Expanded` / `XML Collapsed`) expands the raw event XML. The XML is resolved on demand the first time the toggle is opened for a given event — the placeholder text is `Resolving XML...` until resolution completes. Events with no XML available render `No XML available for this event.`.

The pane can be resized vertically by dragging the splitter at the top.

[Docs home](Home.md)
