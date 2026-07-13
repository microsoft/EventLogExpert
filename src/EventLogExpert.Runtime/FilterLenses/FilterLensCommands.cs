// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.FilterLenses;

internal sealed class FilterLensCommands(IDispatcher dispatcher) : IFilterLensCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void ClearLenses() => _dispatcher.Dispatch(new ClearFilterLensesAction());

    public void RemoveLens(FilterLens lens) => _dispatcher.Dispatch(new RemoveFilterLensAction(lens));

    public void ShowParentActivity(Guid? relatedActivityId, string? originLog = null)
    {
        if (relatedActivityId is { } id)
        {
            PushLens(FilterLensFactory.ForActivityId(id, originLog, label: $"Parent Activity = {id}"));
        }
    }

    public void ShowRelatedByActivityId(Guid? activityId, string? originLog = null)
    {
        if (activityId is { } id) { PushLens(FilterLensFactory.ForActivityId(id, originLog)); }
    }

    public void ShowRelatedByRelatedActivityId(Guid? relatedActivityId, string? originLog = null)
    {
        if (relatedActivityId is { } id) { PushLens(FilterLensFactory.ForRelatedActivityId(id, originLog)); }
    }

    private void PushLens(FilterLens? lens)
    {
        if (lens != null) { _dispatcher.Dispatch(new PushFilterLensAction(lens)); }
    }
}
