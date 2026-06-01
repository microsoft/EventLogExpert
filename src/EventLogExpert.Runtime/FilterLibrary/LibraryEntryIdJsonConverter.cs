// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class LibraryEntryIdJsonConverter : JsonConverter<LibraryEntryId>
{
    public override LibraryEntryId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();

        return string.IsNullOrEmpty(raw) ?
            throw new JsonException($"Expected non-empty string for {nameof(LibraryEntryId)}.") :
            new LibraryEntryId(Guid.Parse(raw));
    }

    public override void Write(Utf8JsonWriter writer, LibraryEntryId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value.ToString("D"));
}
