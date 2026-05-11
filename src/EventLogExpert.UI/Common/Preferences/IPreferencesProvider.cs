// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Clipboard;
using EventLogExpert.UI.Filter;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Settings;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Common.Preferences;

public interface IPreferencesProvider
{
    IEnumerable<ColumnName> ColumnOrderPreference { get; set; }

    IDictionary<ColumnName, int> ColumnWidthsPreference { get; set; }

    int DetailsPaneHeightPreference { get; set; }

    IEnumerable<string> DisabledDatabasesPreference { get; set; }

    bool DisplayPaneSelectionPreference { get; set; }

    IEnumerable<ColumnName> EnabledEventTableColumnsPreference { get; set; }

    IEnumerable<string> FavoriteFiltersPreference { get; set; }

    EventCopyFormat KeyboardCopyFormatPreference { get; set; }

    LogLevel LogLevelPreference { get; set; }

    bool PreReleasePreference { get; set; }

    IEnumerable<string> RecentFiltersPreference { get; set; }

    IEnumerable<SavedFilterGroup> SavedFiltersPreference { get; set; }

    Theme ThemePreference { get; set; }

    string TimeZonePreference { get; set; }
}
