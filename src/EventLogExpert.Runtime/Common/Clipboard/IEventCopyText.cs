// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Common.Clipboard;

public interface IEventCopyText
{
    string MarkdownDescriptionHeader { get; }

    string FieldLine(EventCopyFullField field, string value);

    string MarkdownColumnHeader(ColumnName column);
}
