// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Interfaces;

public interface IPreferencesProvider
{
    bool ActivityIdColumnPreference { get; set; }

    bool ComputerNameColumnPreference { get; set; }

    bool DateAndTimeColumnPreference { get; set; }

    IList<string> DisabledDatabasesPreference { get; set; }

    bool DisplayPaneSelectionPreference { get; set; }

    bool EventIdColumnPreference { get; set; }

    IList<string> FavoriteFiltersPreference { get; set; }

    CopyType KeyboardCopyTypePreference { get; set; }

    bool LevelColumnPreference { get; set; }

    LogLevel LogLevelPreference { get; set; }

    bool LogNameColumnPreference { get; set; }

    bool PreReleasePreference { get; set; }

    IList<string> RecentFiltersPreference { get; set; }

    IList<FilterGroupModel> SavedFiltersPreference { get; set; }

    bool SourceColumnPreference { get; set; }

    bool TaskCategoryColumnPreference { get; set; }

    string TimeZonePreference { get; set; }
}
