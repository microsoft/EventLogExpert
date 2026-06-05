// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventLogExpert.Runtime.FilterLibrary;

public static class LibraryEntryTagNormalizer
{
    public const int MaxTagLength = 32;

    public const int MaxTagsPerEntry = 20;

    public static LibraryEntry MigrateBackslashName(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.Name.Contains('\\')) { return entry; }

        var segments = entry.Name.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length <= 1 || !segments.All(LooksLikeHierarchyLabel)) { return entry; }

        var newName = segments[^1];
        var promotedTags = segments[..^1];
        var unionedTags = Normalize(promotedTags.Concat(entry.Tags));

        return entry switch
        {
            LibraryEntrySavedFilter f => f with { Name = newName, Tags = unionedTags },
            LibraryEntryFilterSet fs => fs with { Name = newName, Tags = unionedTags },
            _ => entry,
        };
    }

    public static ImmutableList<string> Normalize(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = ImmutableList.CreateBuilder<string>();

        foreach (var raw in tags)
        {
            if (raw is null) { continue; }

            var trimmed = raw.Trim().ToLowerInvariant();

            if (trimmed.Length == 0) { continue; }

            if (trimmed.Length > MaxTagLength)
            {
                var cut = char.IsHighSurrogate(trimmed[MaxTagLength - 1]) ? MaxTagLength - 1 : MaxTagLength;
                trimmed = trimmed[..cut];
            }

            if (!seen.Add(trimmed)) { continue; }

            if (result.Count >= MaxTagsPerEntry) { break; }

            result.Add(trimmed);
        }

        return result.ToImmutable();
    }

    private static bool LooksLikeHierarchyLabel(string segment) =>
        segment.Length > 0 && segment.All(static ch => char.IsLetterOrDigit(ch) || ch is ' ' or '_' or '-');
}

internal sealed class NullToEmptyImmutableStringListConverter : JsonConverter<ImmutableList<string>>
{
    public override bool HandleNull => true;

    public override ImmutableList<string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) { return []; }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected JSON array or null for tags, got {reader.TokenType}.");
        }

        var builder = ImmutableList.CreateBuilder<string>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) { return builder.ToImmutable(); }

            if (reader.TokenType == JsonTokenType.Null) { continue; }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected string element in tags array, got {reader.TokenType}.");
            }

            var value = reader.GetString();
            if (value is not null) { builder.Add(value); }
        }

        throw new JsonException("Unexpected end of JSON while reading tags array.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        ImmutableList<string> value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartArray();

        foreach (var tag in value) { writer.WriteStringValue(tag); }

        writer.WriteEndArray();
    }
}
