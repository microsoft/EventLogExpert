// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.FilterCache;

public interface IFilterCachePreferencesProvider
{
    IEnumerable<string> FavoriteFiltersPreference { get; set; }

    IEnumerable<string> RecentFiltersPreference { get; set; }
}
