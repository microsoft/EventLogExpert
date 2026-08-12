// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.EventLog;

public interface IRevealFocusSource : IChangeNotifier
{
    EventLocator? Current { get; }
}
