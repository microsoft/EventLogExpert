// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Scenarios.Catalog;
using Fluxor;

namespace EventLogExpert.Runtime.Scenarios;

internal sealed class ScenarioApplyService(BuiltInScenarioRegistry registry, IDispatcher dispatcher) : IScenarioApplyService
{
    public void ApplyInApp(ScenarioDefinition scenario, bool replace)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var filters = registry.BuildFilterSet(scenario);

        if (replace)
        {
            dispatcher.Dispatch(new ReplaceFiltersAction(filters));

            return;
        }

        dispatcher.Dispatch(new MergeFiltersAction(filters));
    }
}
