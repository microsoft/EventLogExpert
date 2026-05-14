// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Filter;

/// <summary>
///     Reads and writes <see cref="SavedFilter" /> JSON. Accepts three persisted shapes:
///     <list type="bullet">
///         <item>
///             <description>
///                 <b>Modern (post-L4b)</b>: <c>{ "Color": n, "ComparisonText": "...", "IsExcluded": b, "BasicFilter": ?,
///                 "Mode": "Basic|Advanced|Cached" }</c>. <c>Mode</c> is authoritative.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>L1..L4a</b>: <c>{ "Color": n, "ComparisonText": "...", "IsExcluded": b, "BasicFilter": ? }</c>. No
///                 <c>Mode</c> field; converter infers <see cref="FilterMode.Basic" /> when <c>BasicFilter</c> is present,
///                 otherwise <see cref="FilterMode.Advanced" /> (legacy disk has no Cached records — those went through
///                 <c>FilterCacheModal</c> and were saved as Advanced filters).
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>Pre-L1 legacy</b>: <c>{ "Color": n, "Comparison": { "Value": "..." }, "IsExcluded": b, "FilterType":
///                 "Basic|Advanced|Cached" }</c>. <c>FilterType</c> maps directly to <see cref="FilterMode" />.
///             </description>
///         </item>
///     </list>
///     Always writes the modern shape (with <c>Mode</c>).
///     <para>
///         The validation/construction step (compile the text, hydrate <c>BasicFilter</c>, run repair-decompose for
///         Basic-mode records whose <c>BasicFilter</c> blob is missing) is delegated to
///         <see cref="SavedFilter.LoadFromPersisted" />. The converter holds the legacy-intent policy only.
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
        FilterMode? modernMode = null;
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
                case "Mode":
                    modernMode = ReadFilterMode(ref reader);
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

        FilterMode mode;

        if (modernMode is { } resolvedMode)
        {
            // Modern record: Mode wins. LoadFromPersisted enforces Mode-vs-BasicFilter consistency
            // (Advanced/Cached force BasicFilter=null regardless).
            mode = resolvedMode;
        }
        else if (legacyFilterTypeSeen)
        {
            mode = legacyFilterType switch
            {
                LegacyFilterType.Basic => FilterMode.Basic,
                LegacyFilterType.Cached => FilterMode.Cached,
                _ => FilterMode.Advanced
            };
        }
        else
        {
            // Neither modern Mode nor legacy FilterType: infer from BasicFilter presence (post-L1, pre-L4b shape).
            mode = basicFilter is not null ? FilterMode.Basic : FilterMode.Advanced;
        }

        return SavedFilter.LoadFromPersisted(text, color, isExcluded, basicFilter, mode);
    }

    public override void Write(Utf8JsonWriter writer, SavedFilter value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Color", (int)value.Color);
        writer.WriteString("ComparisonText", value.ComparisonText);
        writer.WriteBoolean("IsExcluded", value.IsExcluded);
        writer.WriteString("Mode", value.Mode.ToString());

        if (value.BasicFilter is not null)
        {
            writer.WritePropertyName("BasicFilter");
            JsonSerializer.Serialize(writer, value.BasicFilter, options);
        }

        writer.WriteEndObject();
    }

    private static FilterMode ReadFilterMode(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.String when Enum.TryParse<FilterMode>(reader.GetString(), out var parsed) => parsed,
            JsonTokenType.Number => (FilterMode)reader.GetInt32(),
            _ => FilterMode.Advanced
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

    private static LegacyFilterType ReadLegacyFilterType(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.String when Enum.TryParse<LegacyFilterType>(reader.GetString(), out var parsed) => parsed,
            JsonTokenType.Number => (LegacyFilterType)reader.GetInt32(),
            _ => LegacyFilterType.Advanced
        };
}

