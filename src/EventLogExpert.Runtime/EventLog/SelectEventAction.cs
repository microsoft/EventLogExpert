// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.EventLog;

internal sealed record SelectEventAction(
    SelectionEntry Selection,
    bool IsMultiSelect = false,
    bool ShouldStaySelected = false);
