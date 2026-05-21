// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using System.Collections.Concurrent;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class EventLogConcurrencyState
{
    private readonly ConcurrentDictionary<EventLogId, byte> _logsLoadedWithXml = new();

    private long _closeAllToken;
    private long _filterToken;

    public void ClearAllLoadedWithXml() => _logsLoadedWithXml.Clear();

    public void ClearLoadedWithXml(EventLogId logId) => _logsLoadedWithXml.TryRemove(logId, out _);

    public long GetCurrentFilterToken() => Interlocked.Read(ref _filterToken);

    public long GetCurrentReloadToken() => Interlocked.Read(ref _closeAllToken);

    public long InvalidateInFlightFilters() => Interlocked.Increment(ref _filterToken);

    public void InvalidateInFlightReloads() => Interlocked.Increment(ref _closeAllToken);

    public bool IsLoadedWithXml(EventLogId logId) => _logsLoadedWithXml.ContainsKey(logId);

    public void MarkLoadedWithXml(EventLogId logId) => _logsLoadedWithXml[logId] = 0;
}
