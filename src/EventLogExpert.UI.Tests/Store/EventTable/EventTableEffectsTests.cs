// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Store.EventTable;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Store.EventTable;

public sealed class EventTableEffectsTests
{
    [Fact]
    public async Task HandleLoadColumns_ShouldLoadAllColumnsFromPreferences()
    {
        // Arrange
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.DateAndTime,
            ColumnName.Source
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);

        // Act
        await effects.HandleLoadColumns(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(a =>
            a.LoadedColumns.Count == Enum.GetValues<ColumnName>().Length));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldMarkDisabledColumnsAsFalse()
    {
        // Arrange
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);

        // Act
        await effects.HandleLoadColumns(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(a =>
            a.LoadedColumns[ColumnName.Source] == false &&
            a.LoadedColumns[ColumnName.EventId] == false));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldMarkEnabledColumnsAsTrue()
    {
        // Arrange
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.DateAndTime
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);

        // Act
        await effects.HandleLoadColumns(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(a =>
            a.LoadedColumns[ColumnName.Level] == true &&
            a.LoadedColumns[ColumnName.DateAndTime] == true));
    }

    [Fact]
    public async Task HandleLoadColumns_WhenNoColumnsEnabled_ShouldMarkAllAsFalse()
    {
        // Arrange
        var enabledColumns = new List<ColumnName>();

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);

        // Act
        await effects.HandleLoadColumns(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(a =>
            a.LoadedColumns.All(kvp => kvp.Value == false)));
    }

    [Fact]
    public async Task HandleToggleColumn_ShouldOnlyChangeToggledColumn()
    {
        // Arrange
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.DateAndTime,
            ColumnName.Source,
            ColumnName.EventId
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new EventTableAction.ToggleColumn(ColumnName.DateAndTime);

        // Act
        await effects.HandleToggleColumn(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(a =>
            a.LoadedColumns[ColumnName.Level] == true &&
            a.LoadedColumns[ColumnName.DateAndTime] == false &&
            a.LoadedColumns[ColumnName.Source] == true &&
            a.LoadedColumns[ColumnName.EventId] == true));
    }

    [Fact]
    public async Task HandleToggleColumn_ShouldUpdatePreferences()
    {
        // Arrange
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.DateAndTime
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new EventTableAction.ToggleColumn(ColumnName.Source);

        // Act
        await effects.HandleToggleColumn(action, mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).EnabledEventTableColumnsPreference =
            Arg.Is<IEnumerable<ColumnName>>(cols =>
                cols.Contains(ColumnName.Source));
    }

    [Fact]
    public async Task HandleToggleColumn_WhenColumnDisabled_ShouldEnableIt()
    {
        // Arrange
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.DateAndTime,
            ColumnName.Source
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new EventTableAction.ToggleColumn(ColumnName.Level);

        // Act
        await effects.HandleToggleColumn(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(a =>
            a.LoadedColumns[ColumnName.Level] == true &&
            a.LoadedColumns[ColumnName.DateAndTime] == true &&
            a.LoadedColumns[ColumnName.Source] == true));
    }

    [Fact]
    public async Task HandleToggleColumn_WhenColumnEnabled_ShouldDisableIt()
    {
        // Arrange
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.DateAndTime,
            ColumnName.Source
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new EventTableAction.ToggleColumn(ColumnName.Level);

        // Act
        await effects.HandleToggleColumn(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(a =>
            a.LoadedColumns[ColumnName.Level] == false &&
            a.LoadedColumns[ColumnName.DateAndTime] == true &&
            a.LoadedColumns[ColumnName.Source] == true));
    }

    [Fact]
    public async Task HandleToggleColumn_WhenDisabling_ShouldRemoveFromPreferences()
    {
        // Arrange
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.Source,
            ColumnName.DateAndTime
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new EventTableAction.ToggleColumn(ColumnName.Source);

        // Act
        await effects.HandleToggleColumn(action, mockDispatcher);

        // Assert
        var _ = mockPreferencesProvider.Received(1).EnabledEventTableColumnsPreference =
            Arg.Is<IEnumerable<ColumnName>>(cols =>
                cols.Contains(ColumnName.Level) &&
                cols.Contains(ColumnName.DateAndTime) &&
                !cols.Contains(ColumnName.Source) &&
                cols.Count() == 2);
    }

    [Fact]
    public async Task HandleToggleColumn_WhenEnabling_ShouldPersistToPreferences()
    {
        // Arrange
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new EventTableAction.ToggleColumn(ColumnName.Source);

        // Act
        await effects.HandleToggleColumn(action, mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).EnabledEventTableColumnsPreference =
            Arg.Is<IEnumerable<ColumnName>>(cols =>
                cols.Contains(ColumnName.Level) &&
                cols.Contains(ColumnName.Source) &&
                cols.Count() == 2);
    }

    [Fact]
    public async Task HandleUpdateDisplayedEvents_ShouldDispatchUpdateCombinedEvents()
    {
        // Arrange
        var mockDispatcher = Substitute.For<IDispatcher>();

        // Act
        await EventTableEffects.HandleUpdateDisplayedEvents(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventTableAction.UpdateCombinedEvents>());
    }

    [Fact]
    public async Task HandleUpdateTable_ShouldDispatchUpdateCombinedEvents()
    {
        // Arrange
        var mockDispatcher = Substitute.For<IDispatcher>();

        // Act
        await EventTableEffects.HandleUpdateTable(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventTableAction.UpdateCombinedEvents>());
    }

    private static (EventTableEffects effects, IDispatcher mockDispatcher, IPreferencesProvider mockPreferencesProvider)
        CreateEffects(List<ColumnName>? enabledColumns = null)
    {
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.EnabledEventTableColumnsPreference.Returns(enabledColumns ?? []);

        var effects = new EventTableEffects(mockPreferencesProvider);
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher, mockPreferencesProvider);
    }
}
