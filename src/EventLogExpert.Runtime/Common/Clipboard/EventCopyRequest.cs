// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Common.Clipboard;

public sealed record EventCopyRequest(
    ImmutableList<SelectionEntry> Selection,
    SelectionEntry? Focus,
    ImmutableDictionary<ColumnName, bool> EnabledColumns,
    ImmutableList<ColumnName> ColumnOrder,
    EventCopyFormat Format,
    TimeZoneInfo TimeZone);
