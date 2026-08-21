// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using Fluxor;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.ActivityCorrelation;

/// <summary>
///     Drops the correlation view cache when a log closes so it neither retains the closed log's neighborhood nor is
///     repopulated by a build that completes after the close.
/// </summary>
internal sealed class ActivityCorrelationCacheEffects(IActivityCorrelationCacheControl cacheControl)
{
    private readonly IActivityCorrelationCacheControl _cacheControl = cacheControl;

    [EffectMethod(typeof(CloseAllLogsAction))]
    public Task HandleCloseAllLogs(IDispatcher dispatcher)
    {
        _cacheControl.Invalidate();

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(CloseLogAction))]
    public Task HandleCloseLog(IDispatcher dispatcher)
    {
        _cacheControl.Invalidate();

        return Task.CompletedTask;
    }
}
