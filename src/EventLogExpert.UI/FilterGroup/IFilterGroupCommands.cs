// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.FilterGroup;

/// <summary>
///     Host-facing intent API for the FilterGroup slice. Hides slice-internal action records from
///     consumers outside the UI assembly so the IVT grant to <c>EventLogExpert</c> can be dropped.
/// </summary>
public interface IFilterGroupCommands
{
    /// <summary>Loads persisted filter groups from preferences into the FilterGroup store.</summary>
    void LoadGroups();
}
