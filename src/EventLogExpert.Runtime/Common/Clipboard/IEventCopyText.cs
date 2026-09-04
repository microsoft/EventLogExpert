// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Clipboard;

public interface IEventCopyText
{
    string MarkdownDescriptionHeader { get; }

    string FieldLine(EventCopyFullField field, string value);
}
