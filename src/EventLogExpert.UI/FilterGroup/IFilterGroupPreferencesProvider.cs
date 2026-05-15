// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;

namespace EventLogExpert.UI.FilterGroup;

public interface IFilterGroupPreferencesProvider
{
    IEnumerable<SavedFilterGroup> SavedFiltersPreference { get; set; }
}
