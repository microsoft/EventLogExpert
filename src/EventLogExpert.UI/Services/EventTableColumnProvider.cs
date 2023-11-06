// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;

namespace EventLogExpert.UI.Services;

public interface IEventTableColumnProvider
{
    IDictionary<ColumnName, bool> GetColumns();
}

public class EventTableColumnProvider : IEventTableColumnProvider
{
    private readonly IPreferencesProvider _preferencesProvider;

    public EventTableColumnProvider(IPreferencesProvider preferencesProvider) =>
        _preferencesProvider = preferencesProvider;

    public IDictionary<ColumnName, bool> GetColumns() => new Dictionary<ColumnName, bool>
    {
        { ColumnName.Level, _preferencesProvider.LevelColumnPreference },
        { ColumnName.DateAndTime, _preferencesProvider.DateAndTimeColumnPreference },
        { ColumnName.ActivityId, _preferencesProvider.ActivityIdColumnPreference },
        { ColumnName.LogName, _preferencesProvider.LogNameColumnPreference },
        { ColumnName.ComputerName, _preferencesProvider.ComputerNameColumnPreference },
        { ColumnName.Source, _preferencesProvider.SourceColumnPreference },
        { ColumnName.EventId, _preferencesProvider.EventIdColumnPreference },
        { ColumnName.TaskCategory, _preferencesProvider.TaskCategoryColumnPreference },
        { ColumnName.Description, _preferencesProvider.DescriptionColumnPreference }
    };
}
