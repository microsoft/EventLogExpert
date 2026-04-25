// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

/// <summary>
///     Platform-neutral surface invoked by the Blazor menu bar and keyboard shortcut handler.
///     Returns only primitives — never MAUI/WinUI/MenuItem types — so the menu UI can be unit-tested
///     and the platform implementation can evolve independently.
/// </summary>
public interface IMenuActionService
{
    Task CheckForUpdatesAsync();

    void ClearAllFilters();

    Task CloseAllLogsAsync();

    Task CopySelectedAsync(CopyType? copyType);

    void Exit();

    /// <summary>
    ///     Returns the names of every event log channel known to the host. Cached on first call;
    ///     subsequent calls return immediately.
    /// </summary>
    Task<IReadOnlyList<string>> GetOtherLogNamesAsync();

    void LoadNewEvents();

    Task OpenDocsAsync();

    Task OpenFileAsync(bool addLog);

    Task OpenFolderAsync(bool addLog);

    Task OpenIssueAsync();

    Task OpenLiveLogAsync(string logName, bool addLog);

    Task OpenSettingsAsync();

    Task SaveAllFiltersAsync();

    void SetContinuouslyUpdate(bool value);

    Task ShowDebugLogsAsync();

    Task ShowFilterCacheAsync();

    Task ShowFilterGroupsAsync();

    Task ShowReleaseNotesAsync();

    void ToggleShowAllEvents();
}
