// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Files;

public static class FilePickerFileTypes
{
    public static readonly IReadOnlyList<string> Database = [".db", ".zip"];
    public static readonly IReadOnlyList<string> Json = [".json"];
}
