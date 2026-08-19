// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.Stats;

public interface IStatsVisibilitySource : IChangeNotifier
{
    bool IsVisible { get; }
}
