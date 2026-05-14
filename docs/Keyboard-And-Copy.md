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

`Ctrl+C` invokes whichever format `Tools` → `Settings` → `Keyboard Copy Behavior` is set to. The `Ctrl+C` shortcut hint in the `Edit` menu moves to that entry so the keyboard binding is always visible. `Default` is the initial setting.

Multi-selection is honored — every selected row is included in the copied payload in the order they appear.

`Save All Filters` and `Clear All Filters` also live on the `Edit` menu. `Save All Filters` prompts for a `Group Name` (default `New Filter Section\New Filter Group`) and saves the current filter pane as a named filter group — see [Saved Filters](Saved-Filters.md). `Clear All Filters` removes every filter from the pane in one step, including the date filter.

### Menu navigation

The menu bar follows WAI-ARIA menubar conventions:

| Key | Action |
| --- | --- |
| `Alt` (or `F10`) | Move focus to the menu bar. |
| `ArrowLeft` / `ArrowRight` | Move between top-level menus. Wraps. If a menu is open, switches to the new menu. |
| `Home` / `End` | Jump to the first or last top-level menu. |
| `ArrowDown` | Open the focused menu, with focus on the first item. |
| `ArrowUp` | Open the focused menu, with focus on the last item. |
| `Enter` / `Space` | Open the focused menu (alternative to `ArrowDown`). |
| `Escape` | Close the open menu. |

Hovering a different top-level menu while a menu is open switches to that menu — same as Win32 menubars.

### Other shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+O` | `File` → `Open` → `File`. Standalone open; not the `Combine` variant. |
| `Ctrl+H` | Toggle `View` → `Show All Events`. Suspends inclusion-filter evaluation so any event not blocked by an exclusion or by the date filter becomes visible. Toggling again resumes filtering against the same set. |
| `Ctrl+C` | Copy selected events using the `Keyboard Copy Behavior` format. |

[Docs home](Home.md)
