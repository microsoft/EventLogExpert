// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.EventLog;

public readonly record struct SelectionEntry(
    EventLocator OriginHandle,
    EventLocator? CurrentHandle,
    ValueKey? ReloadKey);
