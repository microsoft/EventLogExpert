// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterCache;
using System.Text.Json;

namespace EventLogExpert.Adapters.Settings;

internal sealed class FilterCachePreferencesAdapter : IFilterCachePreferencesProvider
{
    private const string FavoriteFilters = "favorite-filters";
    private const string RecentFilters = "recent-filters";

    public IEnumerable<string> FavoriteFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(FavoriteFilters, "[]")) ?? [];
        set => Preferences.Default.Set(FavoriteFilters, JsonSerializer.Serialize(value));
    }

    public IEnumerable<string> RecentFiltersPreference
    {
        get => JsonSerializer.Deserialize<List<string>>(Preferences.Default.Get(RecentFilters, "[]")) ?? [];
        set => Preferences.Default.Set(RecentFilters, JsonSerializer.Serialize(value));
    }
}
