// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Clipboard;

namespace EventLogExpert.UI.Menu;

public interface IMenuActionService
{
    Task CheckForUpdatesAsync();

    void ClearAllFilters();

    Task CloseAllLogsAsync();

    Task CopySelectedAsync(EventCopyFormat? format);

    void Exit();

    /// <summary>
    ///     Returns the names of every event log channel known to the host. Cached on first call; subsequent calls return
    ///     immediately.
    /// </summary>
    Task<IReadOnlyList<string>> GetOtherLogNamesAsync();

    void LoadNewEvents();

    Task OpenDocsAsync();

    Task OpenFileAsync(bool combineLog);

    Task OpenFolderAsync(bool combineLog);

    Task OpenIssueAsync();

    Task OpenLiveLogAsync(string logName, bool combineLog);

    Task<bool> OpenSettingsAsync();

    Task SaveAllFiltersAsync();

    void SetContinuouslyUpdate(bool value);

    Task ShowDebugLogsAsync();

    Task ShowFilterCacheAsync();

    Task ShowFilterGroupsAsync();

    Task ShowReleaseNotesAsync();

    void ToggleShowAllEvents();
}
