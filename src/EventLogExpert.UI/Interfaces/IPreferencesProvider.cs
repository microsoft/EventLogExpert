// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IPreferencesProvider
{
    IList<string> DisabledDatabasesPreference { get; set; }

    bool DisplayPaneSelectionPreference { get; set; }

    IList<string> FavoriteFiltersPreference { get; set; }

    bool PrereleasePreference { get; set; }

    IList<string> RecentFiltersPreference { get; set; }

    string TimeZonePreference { get; set; }
}
