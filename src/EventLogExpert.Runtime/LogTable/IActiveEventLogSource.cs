// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.LogTable;

public interface IActiveEventLogSource : IChangeNotifier
{
    EventLogId? Current { get; }
}
