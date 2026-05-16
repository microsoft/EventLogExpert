// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.EventLog;

internal sealed record SelectEventAction(
    ResolvedEvent SelectedEvent,
    bool IsMultiSelect = false,
    bool ShouldStaySelected = false);
