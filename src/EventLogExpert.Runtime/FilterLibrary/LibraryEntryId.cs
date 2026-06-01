// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.Runtime.FilterLibrary;

[JsonConverter(typeof(LibraryEntryIdJsonConverter))]
public readonly record struct LibraryEntryId(Guid Value)
{
    public static LibraryEntryId Create() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}
