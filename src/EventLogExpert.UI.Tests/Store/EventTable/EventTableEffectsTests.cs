// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Store.EventTable;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

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
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.LoadedColumns.Count == Enum.GetValues<ColumnName>().Length));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldLoadWidthsFromPreferences()
    {
        // Arrange
        var enabledColumns = new List<ColumnName> { ColumnName.Level };
        var savedWidths = new Dictionary<ColumnName, int>
        {
            { ColumnName.Level, 150 }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        mockPreferencesProvider.ColumnWidthsPreference.Returns(savedWidths);

        // Act
        await effects.HandleLoadColumns(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.ColumnWidths[ColumnName.Level] == 150));
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
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.LoadedColumns[ColumnName.Source] == false &&
            action.LoadedColumns[ColumnName.EventId] == false));
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
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.LoadedColumns[ColumnName.Level] == true &&
            action.LoadedColumns[ColumnName.DateAndTime] == true));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldUseDefaultOrderWhenNotSaved()
    {
        // Arrange
        var enabledColumns = new List<ColumnName> { ColumnName.Level };

        var (effects, mockDispatcher, _) = CreateEffects(enabledColumns);

        // Act
        await effects.HandleLoadColumns(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.ColumnOrder.SequenceEqual(ColumnDefaults.Order)));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldUseDefaultWidthsWhenNotSaved()
    {
        // Arrange
        var enabledColumns = new List<ColumnName> { ColumnName.Level };

        var (effects, mockDispatcher, _) = CreateEffects(enabledColumns);

        // Act
        await effects.HandleLoadColumns(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.ColumnWidths[ColumnName.Level] == ColumnDefaults.GetWidth(ColumnName.Level)));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldUseSavedOrderWhenPresent()
    {
        // Arrange
        var enabledColumns = new List<ColumnName> { ColumnName.Source, ColumnName.Level };
        var savedOrder = new List<ColumnName> { ColumnName.Source, ColumnName.Level };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        mockPreferencesProvider.ColumnOrderPreference.Returns(savedOrder);

        // Act
        await effects.HandleLoadColumns(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.ColumnOrder[0] == ColumnName.Source &&
            action.ColumnOrder[1] == ColumnName.Level));
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
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.LoadedColumns.All(kvp => kvp.Value == false)));
    }

    [Fact]
    public async Task HandleReorderColumn_ShouldPersistToPreferences()
    {
        // Arrange - state reflects post-reducer result (Source moved to index 0)
        var postReducerState = new EventTableState
        {
            ColumnOrder = [ColumnName.Source, ColumnName.Level, ColumnName.DateAndTime]
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(state: postReducerState);

        var action = new EventTableAction.ReorderColumn(ColumnName.Source, ColumnName.Level, false);

        // Act
        await effects.HandleReorderColumn(action, mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).ColumnOrderPreference =
            Arg.Is<IEnumerable<ColumnName>>(order => order.First() == ColumnName.Source);
    }

    [Fact]
    public async Task HandleResetColumnDefaults_ShouldResetAllColumnSettingsToDefaults()
    {
        // Arrange
        var enabledColumns = new List<ColumnName> { ColumnName.Level, ColumnName.Source };
        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);

        // Act
        await effects.HandleResetColumnDefaults(mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).EnabledEventTableColumnsPreference =
            Arg.Is<IEnumerable<ColumnName>>(c => c.SequenceEqual(ColumnDefaults.EnabledColumns));
        _ = mockPreferencesProvider.Received(1).ColumnWidthsPreference =
            Arg.Is<IDictionary<ColumnName, int>>(w => w.Count == 0);
        _ = mockPreferencesProvider.Received(1).ColumnOrderPreference =
            Arg.Is<IEnumerable<ColumnName>>(o => !o.Any());

        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.ColumnWidths.SequenceEqual(ColumnDefaults.Widths) &&
            action.ColumnOrder.SequenceEqual(ColumnDefaults.Order) &&
            action.LoadedColumns[ColumnName.Level] == true &&
            action.LoadedColumns[ColumnName.DateAndTime] == true &&
            action.LoadedColumns[ColumnName.Source] == true &&
            action.LoadedColumns[ColumnName.EventId] == true &&
            action.LoadedColumns[ColumnName.TaskCategory] == true &&
            action.LoadedColumns[ColumnName.ActivityId] == false));
    }

    [Fact]
    public async Task HandleSetColumnWidth_ShouldPersistToPreferences()
    {
        // Arrange
        var postReducerState = new EventTableState
        {
            ColumnWidths = new Dictionary<ColumnName, int>
            {
                { ColumnName.Level, 200 }
            }.ToImmutableDictionary()
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(state: postReducerState);

        var action = new EventTableAction.SetColumnWidth(ColumnName.Level, 200);

        // Act
        await effects.HandleSetColumnWidth(action, mockDispatcher);

        // Assert
        _ = mockPreferencesProvider.Received(1).ColumnWidthsPreference =
            Arg.Is<IDictionary<ColumnName, int>>(width => width[ColumnName.Level] == 200);
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
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.LoadedColumns[ColumnName.Level] == true &&
            action.LoadedColumns[ColumnName.DateAndTime] == false &&
            action.LoadedColumns[ColumnName.Source] == true &&
            action.LoadedColumns[ColumnName.EventId] == true));
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
            Arg.Is<IEnumerable<ColumnName>>(columns =>
                columns.Contains(ColumnName.Source));
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
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.LoadedColumns[ColumnName.Level] == true &&
            action.LoadedColumns[ColumnName.DateAndTime] == true &&
            action.LoadedColumns[ColumnName.Source] == true));
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
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventTableAction.LoadColumnsCompleted>(action =>
            action.LoadedColumns[ColumnName.Level] == false &&
            action.LoadedColumns[ColumnName.DateAndTime] == true &&
            action.LoadedColumns[ColumnName.Source] == true));
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
            Arg.Is<IEnumerable<ColumnName>>(columns =>
                columns.Contains(ColumnName.Level) &&
                columns.Contains(ColumnName.DateAndTime) &&
                !columns.Contains(ColumnName.Source) &&
                columns.Count() == 2);
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
            Arg.Is<IEnumerable<ColumnName>>(columns =>
                columns.Contains(ColumnName.Level) &&
                columns.Contains(ColumnName.Source) &&
                columns.Count() == 2);
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
        CreateEffects(List<ColumnName>? enabledColumns = null, EventTableState? state = null)
    {
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.EnabledEventTableColumnsPreference.Returns(enabledColumns ?? []);
        mockPreferencesProvider.ColumnWidthsPreference.Returns(new Dictionary<ColumnName, int>());
        mockPreferencesProvider.ColumnOrderPreference.Returns([]);

        var mockState = Substitute.For<IState<EventTableState>>();
        mockState.Value.Returns(state ?? new EventTableState());

        var effects = new EventTableEffects(mockPreferencesProvider, mockState);
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher, mockPreferencesProvider);
    }
}
