// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class LibraryEntryIdJsonConverter : JsonConverter<LibraryEntryId>
{
    public override LibraryEntryId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new ImportValidationException(new ImportValidationError.EntryIdExpectedJsonString(reader.TokenType));
        }

        var raw = reader.GetString();

        if (string.IsNullOrEmpty(raw))
        {
            throw new ImportValidationException(new ImportValidationError.EntryIdExpectedNonEmptyString());
        }

        return Guid.TryParse(raw, out var parsed) ?
            new LibraryEntryId(parsed) :
            throw new ImportValidationException(new ImportValidationError.EntryIdInvalidGuid(raw));
    }

    public override void Write(Utf8JsonWriter writer, LibraryEntryId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value.ToString("D"));
}
