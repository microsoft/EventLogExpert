// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Common.Ipc;

/// <summary>
///     Serializes a <see cref="Regex" /> to a JSON object preserving the original pattern, options, and (in most cases)
///     match timeout so the helper-side reconstructed regex behaves identically to the UI-side compiled regex. One
///     deliberate asymmetry applies: <see cref="Regex.InfiniteMatchTimeout" /> writes as JSON <c>null</c> in
///     <c>matchTimeoutMs</c> but reads back as a bounded default (see remarks for the ReDoS-mitigation rationale).
///     A null <see cref="Regex" /> reference is encoded as JSON <c>null</c> at the message level (this converter is not
///     invoked for that case - the surrounding <see cref="JsonConverter{T}" /> infrastructure handles it via the
///     <see cref="JsonConverter{T}.HandleNull" /> protocol).
/// </summary>
/// <remarks>
///     <para>
///         <b>Asymmetric (lossy) null-timeout handling.</b> The writer encodes <see cref="Regex.InfiniteMatchTimeout" />
///         as JSON <c>null</c> (preserving the over-the-wire shape). The reader deliberately does NOT round-trip
///         <see cref="Regex.InfiniteMatchTimeout" />; a null <c>matchTimeoutMs</c> is mapped to a bounded default (
///         <see cref="DefaultMatchTimeoutMs" /> ms) as a ReDoS mitigation - an unbounded regex evaluation in the high-IL
///         helper is catastrophic when combined with a wedged-helper-can't-be-killed scenario. Production callers always
///         emit a finite timeout via <c>FilterRegexFactory</c>, so this asymmetry is only observable for hand-crafted or
///         defensive JSON.
///     </para>
///     <para>
///         <b>Validation.</b> The reader rejects <c>RegexOptions</c> bits outside the documented
///         <see cref="AllowedOptions" /> mask and rejects <c>matchTimeoutMs</c> values outside
///         <c>[1, <see cref="MaxMatchTimeoutMs" />]</c>. <see cref="ArgumentException" /> from the <see cref="Regex" />
///         constructor (invalid pattern or invalid option combinations such as <see cref="RegexOptions.ECMAScript" />
///         combined with <see cref="RegexOptions.RightToLeft" />) is wrapped as <see cref="JsonException" /> so malformed
///         IPC payloads consistently surface as JSON errors.
///     </para>
/// </remarks>
internal sealed class RegexJsonConverter : JsonConverter<Regex>
{
    private const RegexOptions AllowedOptions =
        RegexOptions.IgnoreCase
        | RegexOptions.Multiline
        | RegexOptions.ExplicitCapture
        | RegexOptions.Compiled
        | RegexOptions.Singleline
        | RegexOptions.IgnorePatternWhitespace
        | RegexOptions.RightToLeft
        | RegexOptions.ECMAScript
        | RegexOptions.CultureInvariant
        | RegexOptions.NonBacktracking;
    private const int DefaultMatchTimeoutMs = 1000;
    private const int MaxMatchTimeoutMs = 5000;

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

        if ((regexOptions & ~AllowedOptions) != 0)
        {
            throw new JsonException(
                $"Regex 'options' contains bits outside the allowed mask: 0x{(int)(regexOptions & ~AllowedOptions):X}.");
        }

        TimeSpan matchTimeout;

        if (matchTimeoutMs is null)
        {
            matchTimeout = TimeSpan.FromMilliseconds(DefaultMatchTimeoutMs);
        }
        else if (matchTimeoutMs.Value is < 1 or > MaxMatchTimeoutMs)
        {
            throw new JsonException(
                $"Regex 'matchTimeoutMs' must be in [1, {MaxMatchTimeoutMs}]; got {matchTimeoutMs.Value}.");
        }
        else
        {
            matchTimeout = TimeSpan.FromMilliseconds(matchTimeoutMs.Value);
        }

        try
        {
            return new Regex(pattern, regexOptions, matchTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException(
                $"Regex construction failed (pattern={pattern}, options=0x{(int)regexOptions:X}): {ex.Message}", ex);
        }
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
