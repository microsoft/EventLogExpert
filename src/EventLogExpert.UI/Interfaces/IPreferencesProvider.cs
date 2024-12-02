// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Interfaces;

public interface IPreferencesProvider
{
    IEnumerable<string> DisabledDatabasesPreference { get; set; }

    bool DisplayPaneSelectionPreference { get; set; }

    IEnumerable<ColumnName> EnabledEventTableColumnsPreference { get; set; }

    IEnumerable<string> FavoriteFiltersPreference { get; set; }

    CopyType KeyboardCopyTypePreference { get; set; }

    LogLevel LogLevelPreference { get; set; }

    bool PreReleasePreference { get; set; }

    IEnumerable<string> RecentFiltersPreference { get; set; }

    IEnumerable<FilterGroupModel> SavedFiltersPreference { get; set; }

    string TimeZonePreference { get; set; }
}
