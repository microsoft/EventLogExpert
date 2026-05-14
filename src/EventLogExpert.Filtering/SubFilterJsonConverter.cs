// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventLogExpert.Filtering;

internal sealed class SubFilterJsonConverter : JsonConverter<SubFilter>
{
    public override SubFilter Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject, got {reader.TokenType}.");
        }

        BasicFilterCondition? modernComparison = null;
        BasicFilterCondition? legacyData = null;
        bool joinWithAny = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) { break; }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}.");
            }

            if (reader.GetString() is not { } propertyName)
            {
                throw new JsonException("Expected non-null PropertyName.");
            }

            reader.Read();

            switch (propertyName)
            {
                case "Comparison":
                    modernComparison = ReadCondition(ref reader, options);
                    break;
                case "Data":
                    legacyData = ReadCondition(ref reader, options);
                    break;
                case "JoinWithAny":
                    joinWithAny = reader.TokenType == JsonTokenType.True;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new SubFilter(modernComparison ?? legacyData ?? new BasicFilterCondition(), joinWithAny);
    }

    public override void Write(Utf8JsonWriter writer, SubFilter value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Comparison");
        JsonSerializer.Serialize(writer, value.Comparison, options);
        writer.WriteBoolean("JoinWithAny", value.JoinWithAny);
        writer.WriteEndObject();
    }

    private static BasicFilterCondition? ReadCondition(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null
            ? null
            : JsonSerializer.Deserialize<BasicFilterCondition>(ref reader, options);
}
