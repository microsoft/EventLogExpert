// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.LogTable;

public interface ILogTablePreferencesProvider
{
    IEnumerable<ColumnName> ColumnOrderPreference { get; set; }

    IDictionary<ColumnName, int> ColumnWidthsPreference { get; set; }

    IEnumerable<ColumnName> EnabledEventTableColumnsPreference { get; set; }
}
