// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;

namespace EventLogExpert.UI.Services;

public interface IEventTableColumnProvider
{
    IDictionary<ColumnName, bool> GetColumns();
}

public sealed class EventTableColumnProvider(IPreferencesProvider preferencesProvider) : IEventTableColumnProvider
{
    public IDictionary<ColumnName, bool> GetColumns() => new Dictionary<ColumnName, bool>
    {
        { ColumnName.Level, preferencesProvider.LevelColumnPreference },
        { ColumnName.DateAndTime, preferencesProvider.DateAndTimeColumnPreference },
        { ColumnName.ActivityId, preferencesProvider.ActivityIdColumnPreference },
        { ColumnName.LogName, preferencesProvider.LogNameColumnPreference },
        { ColumnName.ComputerName, preferencesProvider.ComputerNameColumnPreference },
        { ColumnName.Source, preferencesProvider.SourceColumnPreference },
        { ColumnName.EventId, preferencesProvider.EventIdColumnPreference },
        { ColumnName.TaskCategory, preferencesProvider.TaskCategoryColumnPreference }
    };
}
