// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Filter;

/// <summary>
///     Reads and writes <see cref="SavedFilter" /> JSON. Accepts both the legacy persisted shape (
///     <c>{ "Color": n, "Comparison": { "Value": "..." }, "IsExcluded": b }</c>) and the new shape (
///     <c>{ "Color": n, "ComparisonText": "...", "IsExcluded": b, "BasicFilter": ... }</c>).
///     Always writes the new shape.
///     <para>
///         Legacy <c>FilterType</c> Property is still read (locally) so that historical persisted Advanced/Cached filters
///         that happened to carry a stale BasicFilter blob still drop it during load. Newly written JSON omits
///         <c>FilterType</c> entirely; the presence of <c>BasicFilter</c> is the structural marker post-L1.
///     </para>
///     <para>
///         When a persisted <c>ComparisonText</c> fails to compile, the loaded filter retains the text and
///         <see cref="SavedFilter.IsExcluded" /> but has <c>Compiled == null</c> and <c>IsEnabled == false</c> so it is
///         visible in the UI for the user to repair without forcing application start to fail.
///     </para>
/// </summary>
internal sealed class SavedFilterJsonConverter : JsonConverter<SavedFilter>
{
    private enum LegacyFilterType { Basic, Advanced, Cached }

    public override SavedFilter Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject, got {reader.TokenType}.");
        }

        HighlightColor color = HighlightColor.None;
        string? comparisonText = null;
        string? legacyComparisonValue = null;
        bool isExcluded = false;
        LegacyFilterType legacyFilterType = LegacyFilterType.Advanced;
        bool legacyFilterTypeSeen = false;
        BasicFilter? basicFilter = null;

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
                    legacyFilterType = ReadLegacyFilterType(ref reader);
                    legacyFilterTypeSeen = true;
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

        // Stale BasicFilter drop: legacy Advanced/Cached payloads occasionally carried a leftover BasicFilter
        // blob. Honour the historical intent and discard it so the post-L1 invariant (BasicFilter != null
        // implies the filter is structured) is preserved on load.
        if (legacyFilterTypeSeen && legacyFilterType != LegacyFilterType.Basic && basicFilter is not null)
        {
            basicFilter = null;
        }

        // Inverse: legacy Basic with a missing BasicFilter is a corrupt persisted record. Surface a trace and
        // keep the raw text so the user can repair it.
        if (legacyFilterTypeSeen && legacyFilterType == LegacyFilterType.Basic && basicFilter is null)
        {
            Trace.TraceWarning(
                "SavedFilterJsonConverter: persisted Basic filter has no BasicFilter; loading as raw expression. Text='{0}'",
                text);
        }

        CompiledFilter? compiled = null;
        bool compileFailed = false;

        if (!string.IsNullOrEmpty(text) && !FilterCompiler.TryCompile(text, out compiled, out string? error))
        {
            Trace.TraceWarning(
                "SavedFilterJsonConverter: failed to compile persisted filter expression. Text='{0}', Error='{1}'",
                text,
                error);

            compileFailed = true;
        }

        return new SavedFilter
        {
            Color = color,
            ComparisonText = text,
            Compiled = compiled,
            BasicFilter = basicFilter,
            IsEnabled = !compileFailed && compiled is not null,
            IsExcluded = isExcluded
        };
    }

    public override void Write(Utf8JsonWriter writer, SavedFilter value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Color", (int)value.Color);
        writer.WriteString("ComparisonText", value.ComparisonText);
        writer.WriteBoolean("IsExcluded", value.IsExcluded);

        if (value.BasicFilter is not null)
        {
            writer.WritePropertyName("BasicFilter");
            JsonSerializer.Serialize(writer, value.BasicFilter, options);
        }

        writer.WriteEndObject();
    }

    private static LegacyFilterType ReadLegacyFilterType(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.String when Enum.TryParse<LegacyFilterType>(reader.GetString(), out var parsed) => parsed,
            JsonTokenType.Number => (LegacyFilterType)reader.GetInt32(),
            _ => LegacyFilterType.Advanced
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
