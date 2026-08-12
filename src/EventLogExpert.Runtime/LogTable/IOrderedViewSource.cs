// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public interface IOrderedViewSource
{
    event Action<OrderedViewPresentation> Updated;

    OrderedViewPresentation Current { get; }
}
