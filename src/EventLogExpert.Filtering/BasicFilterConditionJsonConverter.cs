// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventLogExpert.Filtering;

/// <summary>
///     Reads and writes <see cref="BasicFilterCondition" /> JSON. Accepts both the legacy persisted format (<c>Category</c>
///     + <c>Evaluator</c>; numeric or member-name enums) and the updated format (<c>Property</c> + <c>Operator</c> +
///     <c>MatchMode</c>). Always writes the updated format with member-name enums.
///     <para>
///         Legacy <c>FilterEvaluator</c> values map to <see cref="ComparisonOperator" /> / <see cref="MatchMode" /> as
///         follows: <c>Equals/Contains/NotEqual/NotContains -> (op, Single)</c>; <c>MultiSelect -> (Equals, Many)</c>.
///     </para>
/// </summary>
internal sealed class BasicFilterConditionJsonConverter : JsonConverter<BasicFilterCondition>
{
    public override BasicFilterCondition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject, got {reader.TokenType}.");
        }

        EventProperty property = default;
        ComparisonOperator op = ComparisonOperator.Equals;
        MatchMode matchMode = MatchMode.Single;
        bool operatorSet = false;
        bool matchModeSet = false;
        string? value = null;
        ImmutableList<string> values = [];

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
                case "Property":
                case "Category":
                    property = ReadEnum(ref reader, default(EventProperty));
                    break;
                case "Operator":
                    op = ReadEnum(ref reader, ComparisonOperator.Equals);
                    operatorSet = true;
                    break;
                case "MatchMode":
                    matchMode = ReadEnum(ref reader, MatchMode.Single);
                    matchModeSet = true;
                    break;
                case "Evaluator":
                    var (legacyOp, legacyMatchMode) = ReadLegacyEvaluator(ref reader);

                    if (!operatorSet) { op = legacyOp; }

                    if (!matchModeSet) { matchMode = legacyMatchMode; }

                    break;
                case "Value":
                    value = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case "Values":
                    values = ReadStringList(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new BasicFilterCondition
        {
            Property = property,
            Operator = op,
            MatchMode = matchMode,
            Value = value,
            Values = values
        };
    }

    public override void Write(Utf8JsonWriter writer, BasicFilterCondition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Property", value.Property.ToString());
        writer.WriteString("Operator", value.Operator.ToString());
        writer.WriteString("MatchMode", value.MatchMode.ToString());

        writer.WritePropertyName("Value");

        if (value.Value is null) { writer.WriteNullValue(); }
        else { writer.WriteStringValue(value.Value); }

        writer.WritePropertyName("Values");
        writer.WriteStartArray();

        foreach (var item in value.Values) { writer.WriteStringValue(item); }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static TEnum ReadEnum<TEnum>(ref Utf8JsonReader reader, TEnum fallback) where TEnum : struct, Enum =>
        reader.TokenType switch
        {
            JsonTokenType.Number => Enum.IsDefined(typeof(TEnum), reader.GetInt32())
                ? (TEnum)Enum.ToObject(typeof(TEnum), reader.GetInt32())
                : fallback,
            JsonTokenType.String when Enum.TryParse<TEnum>(reader.GetString(), out var parsed) => parsed,
            _ => fallback
        };

    private static (ComparisonOperator Operator, MatchMode MatchMode) ReadLegacyEvaluator(ref Utf8JsonReader reader)
    {
        // Legacy FilterEvaluator: 0=Equals, 1=Contains, 2=NotEqual, 3=NotContains, 4=MultiSelect.
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt32() switch
            {
                0 => (ComparisonOperator.Equals, MatchMode.Single),
                1 => (ComparisonOperator.Contains, MatchMode.Single),
                2 => (ComparisonOperator.NotEqual, MatchMode.Single),
                3 => (ComparisonOperator.NotContains, MatchMode.Single),
                4 => (ComparisonOperator.Equals, MatchMode.Many),
                _ => (ComparisonOperator.Equals, MatchMode.Single)
            };
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() switch
            {
                "Equals" => (ComparisonOperator.Equals, MatchMode.Single),
                "Contains" => (ComparisonOperator.Contains, MatchMode.Single),
                "NotEqual" or "Not Equal" => (ComparisonOperator.NotEqual, MatchMode.Single),
                "NotContains" or "Not Contains" => (ComparisonOperator.NotContains, MatchMode.Single),
                "MultiSelect" or "Multi Select" => (ComparisonOperator.Equals, MatchMode.Many),
                _ => (ComparisonOperator.Equals, MatchMode.Single)
            };
        }

        return (ComparisonOperator.Equals, MatchMode.Single);
    }

    private static ImmutableList<string> ReadStringList(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null) { return []; }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected StartArray for Values, got {reader.TokenType}.");
        }

        var builder = ImmutableList.CreateBuilder<string>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) { break; }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected String inside Values array, got {reader.TokenType}.");
            }

            if (reader.GetString() is { } item) { builder.Add(item); }
        }

        return builder.ToImmutable();
    }
}
