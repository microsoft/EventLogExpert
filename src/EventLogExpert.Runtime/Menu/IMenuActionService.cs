// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Export;

namespace EventLogExpert.Runtime.Menu;

public interface IMenuActionService
{
    Task CheckForUpdatesAsync();

    Task CloseAllLogsAsync();

    Task CopySelectedAsync(EventCopyFormat? format);

    void Exit();

    Task ExportEventsAsync(ExportFormat format);

    void LoadNewEvents();

    Task<bool> OpenDatabaseToolsAsync();

    Task OpenDocsAsync();

    Task OpenFileAsync(bool combineLog);

    Task OpenFolderAsync(bool combineLog);

    Task OpenIssueAsync();

    Task OpenLiveLogAsync(string logName, bool combineLog);

    Task<OpenLogsBatchResult> OpenLiveLogsAsync(
        IEnumerable<string> logNames,
        bool combineLog,
        bool showInlineAlerts = true);

    Task<OpenLogsBatchResult> OpenLogFilesAsync(
        IEnumerable<string> filePaths,
        bool combineLog,
        bool showInlineAlerts = true);

    Task<bool> OpenSettingsAsync();

    Task SaveFiltersAsFilterSetAsync();

    void SetAllGroupsCollapsed(bool collapsed);

    void SetContinuouslyUpdate(bool value);

    void SetHistogramVisible(bool value);

    Task ShowDebugLogsAsync();

    Task ShowReleaseNotesAsync();

    void ToggleGroupSortDirection();

    void ToggleShowAllEvents();
}
