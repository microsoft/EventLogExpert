// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Interfaces;

public interface IPreferencesProvider
{
    IList<string> DisabledDatabasesPreference { get; set; }

    bool DisplayPaneSelectionPreference { get; set; }

    IList<ColumnName> EnabledEventTableColumnsPreference { get; set; }

    IList<string> FavoriteFiltersPreference { get; set; }

    CopyType KeyboardCopyTypePreference { get; set; }

    LogLevel LogLevelPreference { get; set; }

    bool PreReleasePreference { get; set; }

    IList<string> RecentFiltersPreference { get; set; }

    IList<FilterGroupModel> SavedFiltersPreference { get; set; }

    string TimeZonePreference { get; set; }
}
