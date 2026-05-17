// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterGroup;

public interface IFilterGroupPreferencesProvider
{
    IEnumerable<SavedFilterGroup> SavedFiltersPreference { get; set; }
}
