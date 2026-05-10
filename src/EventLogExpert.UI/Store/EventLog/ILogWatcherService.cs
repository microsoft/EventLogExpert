// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Store.EventLog;

public interface ILogWatcherService
{
    void AddLog(string logName, string? bookmark, bool renderXml = false);

    Task RemoveAllAsync();

    Task RemoveLogAsync(string logName);
}
