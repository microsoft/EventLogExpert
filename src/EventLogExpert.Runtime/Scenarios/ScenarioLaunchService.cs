// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Scenarios.Catalog;
using Fluxor;

namespace EventLogExpert.Runtime.Scenarios;

internal sealed class ScenarioLaunchService(
    BuiltInScenarioRegistry registry,
    IMenuActionService menuActionService,
    IState<FilterPaneState> filterPaneState,
    IState<HistogramState> histogramState,
    IDispatcher dispatcher) : IScenarioLaunchService
{
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IState<FilterPaneState> _filterPaneState = filterPaneState;
    private readonly IState<HistogramState> _histogramState = histogramState;
    private readonly IMenuActionService _menuActionService = menuActionService;
    private readonly BuiltInScenarioRegistry _registry = registry;

    public async Task<ScenarioLaunchResult> LaunchAsync(
        ScenarioDefinition scenario,
        DateFilter? dateWindow,
        bool combineLog = false)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var priorFilterState = _filterPaneState.Value;

        var filters = _registry.BuildFilterSet(scenario);

        _dispatcher.Dispatch(new ReplaceFiltersAction(filters));
        _dispatcher.Dispatch(new SetFilterDateRangeAction(dateWindow));

        var result = await _menuActionService.OpenLiveLogsAsync(scenario.Channels, combineLog);

        if (result.Opened == 0 && !combineLog)
        {
            _dispatcher.Dispatch(new CloseAllLogsAction());
            _dispatcher.Dispatch(new RestoreFilterPaneStateAction(priorFilterState));
        }
        else if (scenario.ActivatesTimeline && !_histogramState.Value.IsVisible)
        {
            _menuActionService.SetHistogramVisible(true);
        }

        return new ScenarioLaunchResult(result.Opened, result.Empty, result.Failed);
    }
}
