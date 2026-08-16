// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;

namespace EventLogExpert.Runtime.LogTable;

public sealed record ColumnDescriptor(
    EventFieldId FieldId,
    EventProperty? FilterProperty,
    Func<ResolvedEvent, ColumnFormatContext, string> CellText,
    Func<ResolvedEvent, ColumnFormatContext, string>? GroupText = null);
