// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.EventTable;

public sealed record ToggleLoadingAction(EventLogId LogId);
