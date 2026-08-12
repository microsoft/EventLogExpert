// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

internal static class Faults
{
    internal static OrderedViewDisplayFaultedAction Any =>
        new(new InvalidOperationException("the engine failed"));
}
