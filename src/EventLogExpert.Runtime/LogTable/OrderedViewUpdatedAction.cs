// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable.OrderedView;

namespace EventLogExpert.Runtime.LogTable;

internal sealed record OrderedViewUpdatedAction(OrderedViewUpdate Update);
