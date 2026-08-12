// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.EventLog;

public interface IEventLogCommands
{
    void CloseAllLogs();

    void CloseLog(EventLogId logId, string logName);

    void ConsumeRevealFocus(EventLocator target);

    void LoadNewEvents();

    void OpenLog(string logName, LogPathType logPathType, CancellationToken token = default);

    void SetContinuouslyUpdate(bool continuouslyUpdate);

    void SetSelectedEvents(IReadOnlyCollection<SelectionEntry> selection, SelectionEntry? focus);
}
