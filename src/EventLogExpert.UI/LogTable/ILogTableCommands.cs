// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.LogTable;

/// <summary>
///     Host-facing intent API for the LogTable slice. Hides slice-internal action records from consumers outside the
///     UI assembly so the IVT grant to <c>EventLogExpert</c> can be dropped.
/// </summary>
public interface ILogTableCommands
{
    /// <summary>Loads persisted column visibility, widths, and order into the LogTable store.</summary>
    void LoadColumns();

    /// <summary>Resets enabled columns, widths, and order to factory defaults.</summary>
    void ResetColumnDefaults();

    /// <summary>Flips the current sort direction on the active sort column.</summary>
    void ToggleSortDirection();
}
