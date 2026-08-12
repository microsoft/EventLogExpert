// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal readonly record struct OrderKey(EventLocator Locator);
