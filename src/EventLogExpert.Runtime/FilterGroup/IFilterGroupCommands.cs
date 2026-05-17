// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterGroup;

public interface IFilterGroupCommands
{
    /// <summary>Adds a new (typically empty / editing) filter group.</summary>
    void AddGroup(SavedFilterGroup? group = null);

    /// <summary>Imports <paramref name="groups" /> into the FilterGroup store.</summary>
    void ImportGroups(IEnumerable<SavedFilterGroup> groups);

    /// <summary>Loads persisted filter groups from preferences into the FilterGroup store.</summary>
    void LoadGroups();

    /// <summary>Removes the filter with <paramref name="id" /> from the group identified by <paramref name="parentId" />.</summary>
    void RemoveFilter(FilterGroupId parentId, FilterId id);

    /// <summary>Removes the group identified by <paramref name="id" />.</summary>
    void RemoveGroup(FilterGroupId id);

    /// <summary>Adds or replaces <paramref name="filter" /> in the group identified by <paramref name="parentId" />.</summary>
    void SetFilter(FilterGroupId parentId, SavedFilter filter);

    /// <summary>Replaces the group whose id matches <paramref name="group" />.Id with <paramref name="group" />.</summary>
    void SetGroup(SavedFilterGroup group);

    /// <summary>
    ///     Toggles whether the filter with <paramref name="id" /> in <paramref name="parentId" /> excludes matching
    ///     events.
    /// </summary>
    void ToggleFilterExcluded(FilterGroupId parentId, FilterId id);

    /// <summary>Toggles the expanded/collapsed state of the group identified by <paramref name="id" />.</summary>
    void ToggleGroup(FilterGroupId id);
}
