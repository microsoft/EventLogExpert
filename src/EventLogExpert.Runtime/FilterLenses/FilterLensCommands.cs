// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.FilterLenses;

internal sealed class FilterLensCommands(IDispatcher dispatcher) : IFilterLensCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void ClearLenses() => _dispatcher.Dispatch(new ClearFilterLensesAction());

    public void RemoveLens(FilterLens lens) => _dispatcher.Dispatch(new RemoveFilterLensAction(lens));

    public void ShowRelatedByActivityId(Guid? activityId, string? originLog = null)
    {
        if (activityId is not { } id) { return; }

        if (FilterLensFactory.ForActivityId(id, originLog) is { } lens)
        {
            _dispatcher.Dispatch(new PushFilterLensAction(lens));
        }
    }
}
