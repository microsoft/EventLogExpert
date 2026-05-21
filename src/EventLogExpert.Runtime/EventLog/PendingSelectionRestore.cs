// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.EventLog;

internal sealed record PendingSelectionRestore(IReadOnlySet<long> SelectedIds, long? SelectedId);
