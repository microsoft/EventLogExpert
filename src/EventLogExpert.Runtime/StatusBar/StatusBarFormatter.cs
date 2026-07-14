// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.StatusBar;

/// <summary>
///     Pure presentation helpers for the status bar: the active-source label, the total/shown/selected count phrase,
///     the filter-lens indicator tooltip, and the coarse screen-reader activity announcement. Kept free of Fluxor so the
///     UI component stays thin and the logic is unit-testable in isolation, mirroring <c>EventTableColumnFormatter</c>.
///     Count formatting uses the current culture (thousands separators); tests pin the culture.
/// </summary>
public static class StatusBarFormatter
{
    /// <summary>
    ///     Tooltip for the filter-lens indicator that names the narrowing mechanism the breadcrumb/pane owns the detail
    ///     of: "Filter active", "N lenses", or "Filter + N lenses". Returns <see langword="null" /> when nothing narrows (the
    ///     indicator is then hidden).
    /// </summary>
    public static string? FilterIndicatorTooltip(bool persistentActive, int lensCount)
    {
        var lensText = lensCount switch
        {
            <= 0 => null,
            1 => "1 lens",
            _ => $"{lensCount} lenses"
        };

        return (persistentActive, lensText) switch
        {
            (true, null) => "Filter active",
            (true, not null) => $"Filter + {lensText}",
            (false, not null) => lensText,
            (false, null) => null
        };
    }

    /// <summary>
    ///     The coarse activity label announced to screen readers (via the single polite status region). It changes only
    ///     on state transitions, so per-tick loading/buffer counts - which render as silent visual siblings - never announce.
    ///     Priority puts a load error first: <paramref name="resolverStatus" /> is only ever an error message or empty, so it
    ///     must surface even while another log is still loading; then buffer-full, then loading, then continuous updating.
    /// </summary>
    public static string FormatActivityAnnouncement(
        bool isLoading,
        bool bufferFull,
        bool continuouslyUpdating,
        string resolverStatus)
    {
        if (!string.IsNullOrEmpty(resolverStatus)) { return resolverStatus; }

        if (bufferFull) { return "Buffer full"; }

        if (isLoading) { return "Loading"; }

        return continuouslyUpdating ? "Continuously updating" : string.Empty;
    }

    /// <summary>
    ///     "1,234 events" normally, "200 of 1,234 shown" when the effective (base intersect lenses) filter narrows the
    ///     view, plus " {middot} 3 selected" only for a multi-select (<paramref name="selectedCount" /> &gt;= 2 - a single
    ///     click already selects one row, so surfacing "1 selected" would be near-omnipresent noise).
    /// </summary>
    public static string FormatCounts(int total, int shown, bool isFiltered, int selectedCount)
    {
        var head = isFiltered ? $"{shown:N0} of {total:N0} shown" : $"{total:N0} events";

        return selectedCount >= 2 ? $"{head} \u00b7 {selectedCount:N0} selected" : head;
    }

    /// <summary>
    ///     The active tab's source label: a channel's <see cref="LogView.LogName" />, an opened file's base name, a named
    ///     group's name, "All logs (N)" for the implicit all-logs view (N = open per-log tabs), or "Combined (N logs)" for an
    ///     unnamed group. Remote/computer origin is deliberately omitted (<see cref="LogView.ComputerName" /> is the event's
    ///     origin machine, not the connection source). Returns "No log open" when there is no active view.
    /// </summary>
    public static string FormatSource(
        LogView? active,
        IReadOnlyList<LogView> eventTables,
        IReadOnlyList<LogTabGroup> groups)
    {
        if (active is null) { return "No log open"; }

        if (active.GroupId is not { } groupId)
        {
            return active.FileName is { } fileName ? Path.GetFileName(fileName) : active.LogName;
        }

        if (groupId.IsAll)
        {
            var openLogs = 0;

            foreach (var table in eventTables)
            {
                if (!table.IsCombined) { openLogs++; }
            }

            return $"All logs ({openLogs})";
        }

        var group = groups.FirstOrDefault(candidate => candidate.Id == groupId);

        if (group is null) { return "Combined"; }

        return string.IsNullOrEmpty(group.Name) ? $"Combined ({group.MemberIds.Count} logs)" : group.Name;
    }
}
