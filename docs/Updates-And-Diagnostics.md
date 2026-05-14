# [EventLogExpert](Home.md)

## Updates and Diagnostics

The `Help` menu hosts everything related to the build you're running and its diagnostic surface.

### Docs

Opens this docs site in the default browser.

### Submit an Issue

Opens the GitHub issue tracker in the default browser, with the new-issue form pre-selected. No data leaves the app — the form is blank, you fill it in.

### Check for Updates

Triggers an immediate check against the GitHub releases feed. Stable releases are always considered. When `Tools` → `Settings` → `Pre-release Builds` is enabled, pre-release tags are also considered.

The `.appinstaller` distributed with each release also wires up app-installer-driven background update checks on every launch, so this entry is mostly for "what's the latest right now" — the app finds new releases on its own.

### Release Notes

Opens the Release Notes modal, which renders the markdown body of the published GitHub release for the running build. If the release body for the current version couldn't be fetched (no network, version not found), an alert titled `Release Notes Failure` appears instead and the modal stays closed.

### View Logs

Opens the Debug Log modal — the in-app view of the rolling diagnostic log written by the running session. The same log is also accessible as a file under the per-user app data directory; `View Logs` is the in-app surface.

<!-- screenshot: debug-log-modal --> ![Debug Log modal](.images/debug-log-modal.png)

Filter bar:

| Control | Behavior |
| --- | --- |
| `Level` operator | `Equals`, `Not Equal`, or `Multi Select`. |
| `Level` value | Single dropdown when the operator is `Equals` or `Not Equal` (with `All` to disable the filter); multi-select dropdown when the operator is `Multi Select`. |
| `Filter messages...` | Free-text substring filter applied to the message column. Case-insensitive. |

Footer:

| Control | Behavior |
| --- | --- |
| `Export` | Saves the currently-filtered rows to a file (file dialog). |
| `Copy` | Copies the currently-filtered rows to the clipboard. |
| `<n> of <m> entries` | Live counter — filtered rows over total rows. |
| `Refresh` | Re-reads the on-disk log file. |
| `Clear` | Truncates the on-disk log file and the in-memory view. There is no confirmation. |

Set `Tools` → `Settings` → `Logging Level` to a more verbose value before reproducing an issue you intend to report; both `Export` and `Copy` honor whatever the filter bar is currently showing, so narrow to the relevant rows first.

[Docs home](Home.md)
