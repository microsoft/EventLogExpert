// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.EventLog;

public interface ILogWatcherService
{
    void AddLog(string logName, EventLogId logId, string? bookmark, bool renderXml = false);

    Task RemoveAllAsync();

    Task RemoveLogAsync(string logName, EventLogId logId);
}
