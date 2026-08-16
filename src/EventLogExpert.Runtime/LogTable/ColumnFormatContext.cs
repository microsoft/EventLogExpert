// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public readonly record struct ColumnFormatContext(TimeZoneInfo TimeZone, string? DateTimeFormat = null);
