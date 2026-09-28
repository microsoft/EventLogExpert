// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Export;

public interface IEventTableExportText
{
    string DescriptionHeader { get; }

    string ColumnHeader(ColumnName column, TimeZoneInfo timeZone);
}
