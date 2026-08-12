// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.EventLog;

public interface IEventFocusSource : IChangeNotifier
{
    SelectionEntry? Current { get; }
}
