// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class EffectsTests
{
    private static readonly ColumnDefaults s_columnDefaults = new();

    [Fact]
    public async Task HandleLoadColumns_ShouldLoadAllColumnsFromPreferences()
    {
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.DateAndTime,
            ColumnName.Source
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);

        await effects.HandleLoadColumns(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.LoadedColumns.Count == Enum.GetValues<ColumnName>().Length));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldLoadWidthsFromPreferences()
    {
        var enabledColumns = new List<ColumnName> { ColumnName.Level };
        var savedWidths = new Dictionary<ColumnName, int>
        {
            { ColumnName.Level, 150 }
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        mockPreferencesProvider.ColumnWidthsPreference.Returns(savedWidths);

        await effects.HandleLoadColumns(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.ColumnWidths[ColumnName.Level] == 150));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldMarkDisabledColumnsAsFalse()
    {
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);

        await effects.HandleLoadColumns(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.LoadedColumns[ColumnName.Source] == false &&
            action.LoadedColumns[ColumnName.EventId] == false));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldMarkEnabledColumnsAsTrue()
    {
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.DateAndTime
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);

        await effects.HandleLoadColumns(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.LoadedColumns[ColumnName.Level] == true &&
            action.LoadedColumns[ColumnName.DateAndTime] == true));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldUseDefaultOrderWhenNotSaved()
    {
        var enabledColumns = new List<ColumnName> { ColumnName.Level };

        var (effects, mockDispatcher, _) = CreateEffects(enabledColumns);

        await effects.HandleLoadColumns(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.ColumnOrder.SequenceEqual(s_columnDefaults.ColumnOrder)));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldUseDefaultWidthsWhenNotSaved()
    {
        var enabledColumns = new List<ColumnName> { ColumnName.Level };

        var (effects, mockDispatcher, _) = CreateEffects(enabledColumns);

        await effects.HandleLoadColumns(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.ColumnWidths[ColumnName.Level] == s_columnDefaults.GetColumnWidth(ColumnName.Level)));
    }

    [Fact]
    public async Task HandleLoadColumns_ShouldUseSavedOrderWhenPresent()
    {
        var enabledColumns = new List<ColumnName> { ColumnName.Source, ColumnName.Level };
        var savedOrder = new List<ColumnName> { ColumnName.Source, ColumnName.Level };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        mockPreferencesProvider.ColumnOrderPreference.Returns(savedOrder);

        await effects.HandleLoadColumns(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.ColumnOrder[0] == ColumnName.Source &&
            action.ColumnOrder[1] == ColumnName.Level));
    }

    [Fact]
    public async Task HandleLoadColumns_WhenNoColumnsEnabled_ShouldMarkAllAsFalse()
    {
        var enabledColumns = new List<ColumnName>();

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);

        await effects.HandleLoadColumns(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.LoadedColumns.All(kvp => kvp.Value == false)));
    }

    [Fact]
    public async Task HandleReorderColumn_ShouldPersistToPreferences()
    {
        var postReducerState = new LogTableState
        {
            ColumnOrder = [ColumnName.Source, ColumnName.Level, ColumnName.DateAndTime]
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(state: postReducerState);

        var action = new ReorderColumnAction(ColumnName.Source, ColumnName.Level, false);

        await effects.HandleReorderColumn(action, mockDispatcher);

        _ = mockPreferencesProvider.Received(1).ColumnOrderPreference =
            Arg.Is<IEnumerable<ColumnName>>(order => order != null && order.First() == ColumnName.Source);
    }

    [Fact]
    public async Task HandleResetColumnDefaults_ShouldResetAllColumnSettingsToDefaults()
    {
        var enabledColumns = new List<ColumnName> { ColumnName.Level, ColumnName.Source };
        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);

        await effects.HandleResetColumnDefaults(mockDispatcher);

        _ = mockPreferencesProvider.Received(1).EnabledEventTableColumnsPreference =
            Arg.Is<IEnumerable<ColumnName>>(columns =>
                columns != null && columns.SequenceEqual(s_columnDefaults.EnabledColumns));
        _ = mockPreferencesProvider.Received(1).ColumnWidthsPreference =
            Arg.Is<IDictionary<ColumnName, int>>(widths => widths != null && widths.Count == 0);
        _ = mockPreferencesProvider.Received(1).ColumnOrderPreference =
            Arg.Is<IEnumerable<ColumnName>>(order => order != null && !order.Any());

        var expectedWidths = s_columnDefaults.ColumnWidths.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.ColumnWidths.Count == expectedWidths.Count &&
            action.ColumnWidths.All(kvp =>
                expectedWidths.ContainsKey(kvp.Key) && expectedWidths[kvp.Key] == kvp.Value) &&
            action.ColumnOrder.SequenceEqual(s_columnDefaults.ColumnOrder) &&
            action.LoadedColumns[ColumnName.Level] == true &&
            action.LoadedColumns[ColumnName.DateAndTime] == true &&
            action.LoadedColumns[ColumnName.Source] == true &&
            action.LoadedColumns[ColumnName.EventId] == true &&
            action.LoadedColumns[ColumnName.TaskCategory] == true &&
            action.LoadedColumns[ColumnName.ActivityId] == false));
    }

    [Fact]
    public async Task HandleSetAllGroupsCollapsed_RaisesTheGroupCollapseNotifier()
    {
        var notifier = new GroupCollapseNotifier(Substitute.For<ITraceLogger>());
        var raised = 0;
        notifier.Requested += () => raised++;

        var logTableState = Substitute.For<IState<LogTableState>>();
        logTableState.Value.Returns(new LogTableState());
        var effects = new Effects(
            Substitute.For<ILogTablePreferencesProvider>(),
            logTableState,
            s_columnDefaults,
            new NoOpColumnResetMigrator(),
            notifier);

        await effects.HandleSetAllGroupsCollapsed(Substitute.For<IDispatcher>());

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task HandleSetColumnWidth_ShouldPersistToPreferences()
    {
        var postReducerState = new LogTableState
        {
            ColumnWidths = new Dictionary<ColumnName, int>
            {
                { ColumnName.Level, 200 }
            }.ToImmutableDictionary()
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(state: postReducerState);

        var action = new SetColumnWidthAction(ColumnName.Level, 200);

        await effects.HandleSetColumnWidth(action, mockDispatcher);

        _ = mockPreferencesProvider.Received(1).ColumnWidthsPreference =
            Arg.Is<IDictionary<ColumnName, int>>(width => width != null && width[ColumnName.Level] == 200);
    }

    [Fact]
    public async Task HandleToggleColumn_ShouldOnlyChangeToggledColumn()
    {
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.DateAndTime,
            ColumnName.Source,
            ColumnName.EventId
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new ToggleColumnAction(ColumnName.DateAndTime);

        await effects.HandleToggleColumn(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.LoadedColumns[ColumnName.Level] == true &&
            action.LoadedColumns[ColumnName.DateAndTime] == false &&
            action.LoadedColumns[ColumnName.Source] == true &&
            action.LoadedColumns[ColumnName.EventId] == true));
    }

    [Fact]
    public async Task HandleToggleColumn_ShouldUpdatePreferences()
    {
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.DateAndTime
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new ToggleColumnAction(ColumnName.Source);

        await effects.HandleToggleColumn(action, mockDispatcher);

        _ = mockPreferencesProvider.Received(1).EnabledEventTableColumnsPreference =
            Arg.Is<IEnumerable<ColumnName>>(columns =>
                columns != null &&
                columns.Contains(ColumnName.Source));
    }

    [Fact]
    public async Task HandleToggleColumn_WhenColumnDisabled_ShouldEnableIt()
    {
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.DateAndTime,
            ColumnName.Source
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new ToggleColumnAction(ColumnName.Level);

        await effects.HandleToggleColumn(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.LoadedColumns[ColumnName.Level] == true &&
            action.LoadedColumns[ColumnName.DateAndTime] == true &&
            action.LoadedColumns[ColumnName.Source] == true));
    }

    [Fact]
    public async Task HandleToggleColumn_WhenColumnEnabled_ShouldDisableIt()
    {
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.DateAndTime,
            ColumnName.Source
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new ToggleColumnAction(ColumnName.Level);

        await effects.HandleToggleColumn(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<LoadColumnsCompletedAction>(action =>
            action != null &&
            action.LoadedColumns[ColumnName.Level] == false &&
            action.LoadedColumns[ColumnName.DateAndTime] == true &&
            action.LoadedColumns[ColumnName.Source] == true));
    }

    [Fact]
    public async Task HandleToggleColumn_WhenDisabling_ShouldRemoveFromPreferences()
    {
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level,
            ColumnName.Source,
            ColumnName.DateAndTime
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new ToggleColumnAction(ColumnName.Source);

        await effects.HandleToggleColumn(action, mockDispatcher);

        var _ = mockPreferencesProvider.Received(1).EnabledEventTableColumnsPreference =
            Arg.Is<IEnumerable<ColumnName>>(columns =>
                columns != null &&
                columns.Contains(ColumnName.Level) &&
                columns.Contains(ColumnName.DateAndTime) &&
                !columns.Contains(ColumnName.Source) &&
                columns.Count() == 2);
    }

    [Fact]
    public async Task HandleToggleColumn_WhenEnabling_ShouldPersistToPreferences()
    {
        var enabledColumns = new List<ColumnName>
        {
            ColumnName.Level
        };

        var (effects, mockDispatcher, mockPreferencesProvider) = CreateEffects(enabledColumns);
        var action = new ToggleColumnAction(ColumnName.Source);

        await effects.HandleToggleColumn(action, mockDispatcher);

        _ = mockPreferencesProvider.Received(1).EnabledEventTableColumnsPreference =
            Arg.Is<IEnumerable<ColumnName>>(columns =>
                columns != null &&
                columns.Contains(ColumnName.Level) &&
                columns.Contains(ColumnName.Source) &&
                columns.Count() == 2);
    }

    private static (Effects effects, IDispatcher mockDispatcher, ILogTablePreferencesProvider mockPreferencesProvider)
        CreateEffects(List<ColumnName>? enabledColumns = null, LogTableState? state = null)
    {
        var mockPreferencesProvider = Substitute.For<ILogTablePreferencesProvider>();
        mockPreferencesProvider.EnabledEventTableColumnsPreference.Returns(enabledColumns ?? []);
        mockPreferencesProvider.ColumnWidthsPreference.Returns(new Dictionary<ColumnName, int>());
        mockPreferencesProvider.ColumnOrderPreference.Returns([]);

        var mockState = Substitute.For<IState<LogTableState>>();
        mockState.Value.Returns(state ?? new LogTableState());

        var effects = new Effects(mockPreferencesProvider, mockState, s_columnDefaults, new NoOpColumnResetMigrator(), new GroupCollapseNotifier(Substitute.For<ITraceLogger>()));
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher, mockPreferencesProvider);
    }
}
