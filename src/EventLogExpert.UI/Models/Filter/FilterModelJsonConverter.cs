// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

/// <summary>
///     Reads and writes <see cref="FilterModel" /> JSON. Accepts both the legacy persisted shape (
///     <c>{ "Color": n, "Comparison": { "Value": "..." }, "IsExcluded": b }</c>) and the new shape (
///     <c>{ "Color": n, "ComparisonText": "...", "IsExcluded": b, "FilterType": "Advanced", "BasicFilter": ... }</c>).
///     Always writes the new shape.
///     <para>
///         When a persisted <c>ComparisonText</c> fails to compile, the loaded filter retains the text and
///         <see cref="FilterModel.IsExcluded" /> but has <c>Compiled == null</c> and <c>IsEnabled == false</c> so it is
///         visible in the UI for the user to repair without forcing application start to fail.
///     </para>
/// </summary>
public sealed class FilterModelJsonConverter : JsonConverter<FilterModel>
{
    public override FilterModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject, got {reader.TokenType}.");
        }

        HighlightColor color = HighlightColor.None;
        string? comparisonText = null;
        string? legacyComparisonValue = null;
        bool isExcluded = false;
        FilterType filterType = FilterType.Advanced;
        BasicFilter? basicFilter = null;
        bool filterTypeSeen = false;

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
                case "Color":
                    color = ReadHighlightColor(ref reader);
                    break;
                case "ComparisonText":
                    comparisonText = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case "Comparison":
                    legacyComparisonValue = ReadLegacyComparisonValue(ref reader);
                    break;
                case "IsExcluded":
                    isExcluded = reader.GetBoolean();
                    break;
                case "FilterType":
                    filterType = ReadFilterType(ref reader);
                    filterTypeSeen = true;
                    break;
                case "BasicFilter":
                    basicFilter = reader.TokenType == JsonTokenType.Null
                        ? null
                        : JsonSerializer.Deserialize<BasicFilter>(ref reader, options);

                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        string text = comparisonText ?? legacyComparisonValue ?? string.Empty;

        if (!filterTypeSeen && string.IsNullOrEmpty(comparisonText) && legacyComparisonValue is not null)
        {
            // Legacy persistence had no FilterType field; preserve the historical default.
            filterType = FilterType.Advanced;
        }

        if (filterType == FilterType.Basic && basicFilter is null)
        {
            Trace.TraceWarning(
                "FilterModelJsonConverter: persisted Basic filter has no BasicFilter; degrading to Advanced. Text='{0}'",
                text);

            filterType = FilterType.Advanced;
        }

        if (filterType != FilterType.Basic)
        {
            // BasicFilter is only meaningful for Basic filters; drop any stale value.
            basicFilter = null;
        }

        CompiledFilter? compiled = null;
        bool compileFailed = false;

        if (!string.IsNullOrEmpty(text) && !FilterCompiler.TryCompile(text, out compiled, out string? error))
        {
            Trace.TraceWarning(
                "FilterModelJsonConverter: failed to compile persisted filter expression. Text='{0}', Error='{1}'",
                text,
                error);

            compileFailed = true;
        }

        return new FilterModel
        {
            Color = color,
            ComparisonText = text,
            Compiled = compiled,
            BasicFilter = basicFilter,
            FilterType = filterType,
            IsEnabled = !compileFailed && compiled is not null,
            IsExcluded = isExcluded
        };
    }

    public override void Write(Utf8JsonWriter writer, FilterModel value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Color", (int)value.Color);
        writer.WriteString("ComparisonText", value.ComparisonText);
        writer.WriteBoolean("IsExcluded", value.IsExcluded);
        writer.WriteString("FilterType", value.FilterType.ToString());

        if (value.FilterType == FilterType.Basic && value.BasicFilter is not null)
        {
            writer.WritePropertyName("BasicFilter");
            JsonSerializer.Serialize(writer, value.BasicFilter, options);
        }

        writer.WriteEndObject();
    }

    private static FilterType ReadFilterType(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.String when Enum.TryParse<FilterType>(reader.GetString(), out var parsed) => parsed,
            JsonTokenType.Number => (FilterType)reader.GetInt32(),
            _ => FilterType.Advanced
        };

    private static HighlightColor ReadHighlightColor(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => (HighlightColor)reader.GetInt32(),
            JsonTokenType.String when Enum.TryParse<HighlightColor>(reader.GetString(), out var parsed) => parsed,
            _ => HighlightColor.None
        };

    private static string? ReadLegacyComparisonValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null) { return null; }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject for legacy 'Comparison', got {reader.TokenType}.");
        }

        string? value = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) { break; }

            if (reader.TokenType != JsonTokenType.PropertyName) { continue; }

            if (reader.GetString() is not { } name)
            {
                throw new JsonException("Expected non-null PropertyName.");
            }

            reader.Read();

            if (name == "Value")
            {
                value = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        return value;
    }
}
