// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.LogTable;

public interface ILogTabBarSource : IChangeNotifier
{
    LogTabBarPresentation Current { get; }
}
