// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Common.Ipc;

/// <summary>
///     Serializes a <see cref="Regex" /> to a JSON object preserving the original pattern, options, and match timeout
///     so the helper-side reconstructed regex behaves identically to the UI-side compiled regex.
///     <see cref="Regex.InfiniteMatchTimeout" /> is encoded as a JSON <c>null</c> in <c>matchTimeoutMs</c>; a null
///     <see cref="Regex" /> reference is encoded as JSON <c>null</c> at the message level (this converter is not invoked
///     for that case - the surrounding <see cref="JsonConverter{T}" /> infrastructure handles it via the
///     <see cref="JsonConverter{T}.HandleNull" /> protocol).
/// </summary>
internal sealed class RegexJsonConverter : JsonConverter<Regex>
{
    public override Regex? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject for Regex; got {reader.TokenType}.");
        }

        string? pattern = null;
        RegexOptions regexOptions = RegexOptions.None;
        int? matchTimeoutMs = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected PropertyName inside Regex; got {reader.TokenType}.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "pattern":
                    pattern = reader.GetString();
                    break;
                case "options":
                    regexOptions = (RegexOptions)reader.GetInt32();
                    break;
                case "matchTimeoutMs":
                    matchTimeoutMs = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt32();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (pattern is null)
        {
            throw new JsonException("Regex JSON object missing required 'pattern' property.");
        }

        TimeSpan matchTimeout = matchTimeoutMs is null
            ? Regex.InfiniteMatchTimeout
            : TimeSpan.FromMilliseconds(matchTimeoutMs.Value);

        return new Regex(pattern, regexOptions, matchTimeout);
    }

    public override void Write(Utf8JsonWriter writer, Regex value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("pattern", value.ToString());
        writer.WriteNumber("options", (int)value.Options);

        if (value.MatchTimeout == Regex.InfiniteMatchTimeout)
        {
            writer.WriteNull("matchTimeoutMs");
        }
        else
        {
            writer.WriteNumber("matchTimeoutMs", (int)value.MatchTimeout.TotalMilliseconds);
        }

        writer.WriteEndObject();
    }
}
