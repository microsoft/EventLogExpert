// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.EventLog;

public interface IFilterAppliedSource : IChangeNotifier
{
    bool IsFilteringEnabled { get; }
}
