# [EventLogExpert](Home.md)

## Opening Logs

The `File` menu is the primary entry point for opening logs. There are three sources, all available under both `File` → `Open` (replace the active log set) and `File` → `Combine` (overlay on top of what's already open). The `Combine` submenu is greyed out until at least one log is open.

`.evtx` files can also be opened by drag-and-drop onto the window or by passing them as command-line arguments at launch. Both routes always combine — they add to the current log set rather than replacing it.

### File

`File` → `Open` → `File` (`Ctrl+O`) opens the system file picker filtered to `.evtx`. Pick one file or many — every selected `.evtx` is loaded concurrently as a separate log. The same applies for `File` → `Combine` → `File`, which adds the picked files to the existing log set.

### Folder

`File` → `Open` → `Folder` opens a folder picker. Every `.evtx` in the picked folder (top-level only, no recursion) is loaded as a separate log.

Use this when you've been handed a folder of `.evtx` exports from one or more machines and want to load the lot in one step.

### Live

`File` → `Open` → `Live` opens a Windows event channel as a live log — the channel is read in real time as opposed to a static snapshot.

The submenu always lists `Application`, `System`, and `Security`. Below them, an `Other Logs` submenu is built from the channels actually registered on the local machine, presented as a hierarchical tree (`Microsoft` → `Windows` → `<provider>` → `<log>`, etc.). The list is discovered once per app session and cached after the first menu open or background prewarm; channels added or removed during the session aren't reflected until the app restarts.

**Admin-only channels.** Some channels can't be read without elevation. When the app isn't running as Administrator, `Security` (and other elevation-gated channels surfaced under `Other Logs`) appear in the menu but are disabled. Re-launching the app as Administrator enables them. The disabled state is the only signal — there's no popup. Anything that opens fine without elevation stays enabled.

### Combining logs

`File` → `Combine` is the same submenu as `Open`, but adds the picked source(s) to the currently-open log set instead of replacing it. With two or more logs open, an extra `Combined` tab appears in the tab strip showing all events from all open logs interleaved by time — see [Viewing Events](Viewing-Events.md).

`Combine` works for any mix of file logs and live channels. Combining `.evtx` exports from machines in different time zones produces a coherent timeline because the event table renders timestamps in the time zone configured under [Settings](Settings.md). A live channel combined with a file log behaves the same — the live side simply keeps appending events as they arrive (when continuously updating) or filling the buffer (when not).

### Live log behavior

Two `View` menu items only matter while a live log is open:

- **`Continuously Update`** is a toggle. When `on`, new events stream into the table as they arrive. When `off`, new events accumulate in a buffer and the table view stays put.
- **`Load New Events`** drains the buffer into the table once. Useful when `Continuously Update` is off but you want to catch up to "now" before going back to a static view.

The status bar surfaces both:

| Status bar token | Condition |
| --- | --- |
| `Continuously Updating` | At least one live log is open and `Continuously Update` is on. |
| `New Events: <n>` | At least one live log is open and `Continuously Update` is off; `<n>` is the buffer size. |
| `Buffer Full` | The new-event buffer reached its cap. Live watchers are stopped while this is set, so no further events arrive until either `Load New Events` drains the buffer or `Continuously Update` is turned on (both clear the buffer and restart the watchers). |

### Close All

`File` → `Close All` removes every open log (file or live) and clears the tab strip. There's no per-tab close from the `File` menu — the per-tab `×` button on each tab in the tab strip closes a single log. Combined tabs can't be closed individually; they disappear automatically when the tab strip drops below two source logs.

### Exit

`File` → `Exit` closes the application.

[Docs home](Home.md)
