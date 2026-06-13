// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLibrary;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class ColumnResetMigrator(ILegacyPreferences preferences, ITraceLogger logger) : IColumnResetMigrator
{
    private const string CompletedValue = "1";
    private const string CompletionKey = "log-table-column-reset-migration-state";

    public void RunMigration()
    {
        try
        {
            // Clear (not rewrite) the two numeric-keyed prefs so they fall back to defaults; column-widths is name-keyed
            // and reorder-safe so it's left intact. Completion flag written last, so an aborted run safely retries.
            preferences.Remove(LogTablePreferenceKeys.EnabledEventTableColumns);
            preferences.Remove(LogTablePreferenceKeys.ColumnOrder);
            preferences.SetString(CompletionKey, CompletedValue);

            logger.Information(
                $"RecordId column-index migration: cleared enabled-columns + column-order preferences (restored to defaults); column-widths preserved.");
        }
        catch (Exception ex)
        {
            logger.Error($"{nameof(ColumnResetMigrator)}.{nameof(RunMigration)} failed; will retry next launch: {ex}");
        }
    }

    public bool ShouldRunMigration()
    {
        try
        {
            return !string.Equals(preferences.GetString(CompletionKey), CompletedValue, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            logger.Error($"{nameof(ColumnResetMigrator)}.{nameof(ShouldRunMigration)} failed; skipping migration this launch: {ex}");

            return false;
        }
    }
}
