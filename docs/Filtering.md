# [EventLogExpert](Home.md)

## Filtering

The Filter pane sits above the event table. Every event in the active log set is evaluated against the applied filters; non-matches are hidden. The pane has five `Add` buttons across the top:

| Button | Adds |
| --- | --- |
| `Add Basic Filter` | A category × evaluator × value comparison against a single resolved-event field. |
| `Add Date Filter` | A `Before` / `After` time-window filter. Only one date filter exists at a time; the button is hidden once one is added. |
| `Add Advanced Filter` | A free-form Dynamic LINQ expression evaluated against `ResolvedEvent`. |
| `Add Cached Filter` | Picks a string from the Filter Cache (Favorites + Recent) and adds it as a Cached filter row. See [Saved Filters](Saved-Filters.md). |
| `Add Exclusion` | A Basic filter row in the excluded state — same category × evaluator × value shape as `Add Basic Filter`, but matching events are hidden instead of shown. Any saved row can be flipped between included and excluded later via the chrome `Exclude` / `Include` button. |

Each saved row carries a chrome strip with `Edit`, `Exclude` (or `Include` when the row is already an exclusion), `Remove`, and a `Disable` / `Enable` toggle. While editing, the chrome shows `Save` and `Cancel`. Non-exclusion rows also show a highlight-color picker (`Highlight Color`) before the comparison content. When any filter is being applied, the pane shows `[Applying Filters]` with a spinner; otherwise it shows `[Active Filters: N]`. The pane can be collapsed via the caret in the top-right corner.

The pane header's right-side icon strip carries the persistence and management actions: `Save Filters as Group` (bookmark-plus icon — prompts for a group name and saves the current filter rows), `Clear All Filters` (trash icon — confirms then removes every filter row, the date filter, and any pending drafts), `Manage Cached Filters` (bookmark icon — opens the [filter cache modal](Saved-Filters.md)), and `Manage Filter Groups` (collection icon — opens the [filter groups modal](Saved-Filters.md)). The save and clear icons announce themselves as disabled when the pane has nothing to act on. See [Keyboard and Copy](Keyboard-And-Copy.md). `View` → `Show All Events` (`Ctrl+H`) suspends evaluation without removing any filters.

### Basic filters

Pick a category, pick an evaluator, then enter or pick a comparison value. Optionally add one or more predicates joined to the parent with `AND` or `OR`.

**Categories** (`FilterCategory`):

| Label | Source field |
| --- | --- |
| `Event ID` | event id (int) |
| `Activity ID` | activity id GUID (string-equal) |
| `Level` | resolved level string (`Information`, `Warning`, `Error`, `Critical`, `Verbose`) |
| `Keywords` | display keywords |
| `Source` | provider name |
| `Task Category` | resolved task name |
| `Process ID` | process id |
| `Thread ID` | thread id |
| `User ID` | SID string |
| `Description` | resolved description text |
| `Xml` | raw XML (forces eager XML resolution; see caveat) |

**Evaluators** (`FilterEvaluator`):

| Label | Behavior |
| --- | --- |
| `Equals` | Exact match. Numeric for ID-typed fields; case-sensitive string compare for everything except `Keywords`, which is case-insensitive. |
| `Contains` | Case-insensitive substring match. |
| `Not Equal` | Negated `Equals` (same case-sensitivity rules). |
| `Not Contains` | Negated `Contains` (case-insensitive). |
| `Multi Select` | Matches any value in the supplied set. The category determines which set is offered (e.g., `Level` → checkboxes for the five level values; `Source` → the providers seen in the active logs). |

Sub-filters live underneath the parent and can be combined freely. `AND` requires the predicate to also match; `OR` matches if either the parent or the predicate matches.

### Date filter

`After` / `Before` timestamps in the configured time zone (see [Settings](Settings.md) → `Time Zone`). Only one date filter is allowed; removing it lets `Add Date Filter` reappear. Right-clicking an event in the table and choosing `Exclude Events Before` / `Exclude Events After` is a shortcut that sets a date filter using the right-clicked event's timestamp as the boundary.

### Advanced filters (Dynamic LINQ)

Free-form expression evaluated against the `ResolvedEvent` record using [Dynamic LINQ](https://dynamic-linq.net/). The placeholder shown in the input is:

```
(Id == 1000 || Id == 1001) && Description.Contains('Fault')
```

Available properties:

| Property | Type | Notes |
| --- | --- | --- |
| `Id` | `int` | Event id. |
| `ActivityId` | `Guid?` | Nullable. |
| `Level` | `string` | `Information`, `Warning`, `Error`, `Critical`, `Verbose`. |
| `Keywords` | `IReadOnlyList<string>` | Use `.Contains("...")`. |
| `KeywordsDisplayName` | `string` | Comma-separated keywords. |
| `Source` | `string` | Provider name. |
| `TaskCategory` | `string` | |
| `ProcessId` | `int?` | |
| `ThreadId` | `int?` | |
| `UserId` | `SecurityIdentifier?` | Use `.ToString()` to compare. |
| `TimeCreated` | `DateTime` | The raw `ResolvedEvent` value — the table and Details pane render it in the configured time zone, but expressions see the underlying value. |
| `LogName` | `string` | Source log channel as reported by the event reader. |
| `OwningLog` | `string` | The file path or live-channel name as displayed in the tab strip. |
| `LogPathType` | `LogPathType` | `File` or `Channel`. |
| `ComputerName` | `string` | |
| `RecordId` | `long?` | |
| `Description` | `string` | Resolved description text. |
| `Xml` | `string` | Raw event XML. **See XML caveat.** |

**XML caveat.** When a filter expression references `Xml`, the underlying `EventLogReader` is opened with XML rendering enabled, which is meaningfully slower than the default. Adding an XML-referencing filter against logs already loaded without XML triggers a one-time re-read of those logs (only the logs that lack XML — logs already loaded with XML are untouched). Removing or disabling an XML filter does not trigger another reload because the in-memory XML is harmless to keep. Filters that don't reference `Xml` operate on already-resolved fields and stay fast.

### Excluded filters

Either an `Add Exclusion` row from the start, or any Basic / Advanced row toggled with the `Exclude` chrome button. Matching events are hidden. Excluded filters are evaluated independently of `View` → `Show All Events`: the show-all toggle disables only the inclusion side, so exclusions and the date filter remain in effect when it's on. The pane header's `Clear All Filters` icon (trash) removes every filter from the pane (including the date filter and exclusions) — there's no built-in way to reversibly suspend everything at once.

### Cached filters

Quick-access strings for repeat use. See [Saved Filters](Saved-Filters.md) for how the cache is populated and managed.

### Highlighting

Each non-excluded filter row exposes a `Highlight Color` picker in its chrome. When set, every event matching that filter is rendered with that background color in the event table. When multiple filters with different colors match the same event, the first matching enabled, non-excluded filter in pane order wins (a filter with `Highlight Color` set to `None` still counts as a match and suppresses any later highlight). Selection styling beats highlight while a row is selected. Highlight colors persist with the filter — saving a group preserves its colors.

[Docs home](Home.md)
