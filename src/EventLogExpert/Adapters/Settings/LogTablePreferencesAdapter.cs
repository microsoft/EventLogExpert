// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;
using System.Text.Json;

namespace EventLogExpert.Adapters.Settings;

internal sealed class LogTablePreferencesAdapter : ILogTablePreferencesProvider
{
    private const string ColumnOrder = LogTablePreferenceKeys.ColumnOrder;
    private const string ColumnWidths = LogTablePreferenceKeys.ColumnWidths;
    private const string EnabledEventTableColumns = LogTablePreferenceKeys.EnabledEventTableColumns;

    public IEnumerable<ColumnName> ColumnOrderPreference
    {
        get => JsonSerializer.Deserialize<List<ColumnName>>(Preferences.Default.Get(ColumnOrder, "[]")) ?? [];
        set => Preferences.Default.Set(ColumnOrder, JsonSerializer.Serialize(value));
    }

    public IDictionary<ColumnName, int> ColumnWidthsPreference
    {
        get => JsonSerializer.Deserialize<Dictionary<ColumnName, int>>(Preferences.Default.Get(ColumnWidths, "{}")) ?? [];
        set => Preferences.Default.Set(ColumnWidths, JsonSerializer.Serialize(value));
    }

    public IEnumerable<ColumnName> EnabledEventTableColumnsPreference
    {
        get =>
            JsonSerializer.Deserialize<List<ColumnName>>(
                Preferences.Default.Get(
                    EnabledEventTableColumns,
                    $"[{ColumnName.Level:D}, {ColumnName.DateAndTime:D}, {ColumnName.Source:D}, {ColumnName.EventId:D}, {ColumnName.TaskCategory:D}]")) ??
            [];
        set => Preferences.Default.Set(EnabledEventTableColumns, JsonSerializer.Serialize(value));
    }
}
