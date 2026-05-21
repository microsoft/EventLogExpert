// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterGroup;
using System.Text.Json;

namespace EventLogExpert.Adapters.Settings;

internal sealed class FilterGroupPreferencesAdapter : IFilterGroupPreferencesProvider
{
    private const string SavedFilters = "saved-filters";

    public IEnumerable<SavedFilterGroup> SavedFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<SavedFilterGroup>>(Preferences.Default.Get(SavedFilters, "[]")) ?? [];
        set => Preferences.Default.Set(SavedFilters, JsonSerializer.Serialize(value));
    }
}
