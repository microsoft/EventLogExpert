# [EventLogExpert](Home.md)

## Keyboard and Copy

### Copy formats

The `Edit` menu always lists all four copy formats:

| Edit menu item | Format | Output |
| --- | --- | --- |
| `Copy Selected` | `Default` | One quoted, space-separated field per visible event-table column, plus the description. |
| `Copy Selected (Simple)` | `Simple` | Five quoted, space-separated fields: level, timestamp, source, event id, description. |
| `Copy Selected (XML)` | `Xml` | The event's XML, pretty-printed when parseable. |
| `Copy Selected (Full)` | `Full` | A multi-line block with labeled fields (`Log Name`, `Source`, `Date`, `Event ID`, `Task Category`, `Level`, `Keywords`, `User`, `Computer`, `Description`, `Event Xml`). |

`Ctrl+C` invokes whichever format `Tools` → `Settings` → `Keyboard Copy Behavior` is set to. The `Ctrl+C` shortcut hint in the `Edit` menu moves to that entry so the keyboard binding is always visible. `Full` is the initial setting on a fresh install.

Multi-selection is honored — every selected row is included in the copied payload in the order they appear.

The persistence and management actions (`Save as Filter Set`, `Clear All Filters`, `Open Filter Library`) live on the [Filter pane](Filtering.md) header's right-side icon strip rather than on the menu bar. `Save as Filter Set` prompts for a name (default `New Filter Set`) and saves the current filter rows as a new filter set in the library — the date filter is not included. `Clear All Filters` shows a confirmation with the count of items being removed, then drops every filter row, the date filter, and any pending drafts. `Open Filter Library` opens the library modal — see [Saved Filters](Saved-Filters.md).

### Menu navigation

The menu bar follows WAI-ARIA menubar conventions. Once a menu-bar button has focus (Tab in from elsewhere or click one open):

| Key | Action |
| --- | --- |
| `ArrowLeft` / `ArrowRight` | Move between top-level menus. Wraps. If a menu is open, switches to the new menu. |
| `Home` / `End` | Jump to the first or last top-level menu. |
| `ArrowDown` | Open the focused menu, with focus on the first item. |
| `ArrowUp` | Open the focused menu, with focus on the last item. |
| `Enter` / `Space` | Open the focused menu (alternative to `ArrowDown`). |
| `Escape` | Close the open menu. |

Hovering a different top-level menu while a menu is open switches to that menu — same as Win32 menubars.

### Grouped event table

When the event table is [grouped](Viewing-Events.md#grouping) it behaves as a tree grid. Alongside the normal selection keys:

| Key | Action |
| --- | --- |
| `ArrowUp` / `ArrowDown` | Move through visible rows, stopping on group headers as well as events. Landing on an event selects it; landing on a header only moves focus, leaving the current selection unchanged. |
| `ArrowLeft` | On an event, move focus to its group header. On an expanded header, collapse the group. |
| `ArrowRight` | On a collapsed header, expand the group. On an expanded header, move to and select its first event. |
| `Enter` | Toggle the focused group header between expanded and collapsed. |
| `Home` / `End` | Move to the first / last visible row, which may be a header (focus only). |
| `Shift` + `ArrowUp` / `ArrowDown` | Extend the selection to the next / previous event, skipping over headers. A range that spans a collapsed group includes that group's hidden events. |
| `Ctrl+A` | Select every event in the table, including events inside collapsed groups. |

Flat (ungrouped) tables keep the standard arrow behavior; `ArrowLeft` / `ArrowRight` are not intercepted there.

### Other shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+O` | `File` → `Open` → `File`. Standalone open; not the `Combine` variant. |
| `Ctrl+H` | Toggle `View` → `Show All Events`. Suspends inclusion-filter evaluation so any event not blocked by an exclusion or by the date filter becomes visible. Toggling again resumes filtering against the same set. |
| `Ctrl+C` | Copy selected events using the `Keyboard Copy Behavior` format. |

[Docs home](Home.md)
