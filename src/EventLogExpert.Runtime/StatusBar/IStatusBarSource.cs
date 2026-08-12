// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.StatusBar;

public interface IStatusBarSource : IChangeNotifier
{
    StatusBarPresentation Current { get; }
}
