// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Runtime;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace EventLogExpert.Filtering.Persistence;

[JsonConverter(typeof(SavedFilterJsonConverter))]
public sealed record SavedFilter
{
    public static readonly SavedFilter Empty = new() { ComparisonText = string.Empty, Compiled = null };

    [JsonIgnore]
    public FilterId Id { get; init; } = FilterId.Create();

    public HighlightColor Color { get; init; } = HighlightColor.None;

    public required string ComparisonText { get; init; }

    [JsonIgnore]
    public required CompiledFilter? Compiled { get; init; }

    public BasicFilter? BasicFilter { get; init; }

    [JsonIgnore]
    public bool IsEnabled { get; init; }

    public bool IsExcluded { get; init; }

    public FilterMode Mode { get; init; } = FilterMode.Advanced;

    public static SavedFilter? TryCreate(
        string comparisonText,
        BasicFilter? basicFilter = null,
        HighlightColor color = HighlightColor.None,
        bool isExcluded = false,
        bool isEnabled = false,
        FilterId? id = null,
        FilterMode mode = FilterMode.Advanced)
    {
        if (basicFilter is not null && mode != FilterMode.Basic)
        {
            throw new ArgumentException(
                $"Supplying a {nameof(BasicFilter)} requires {nameof(mode)} = {nameof(FilterMode.Basic)}; got '{mode}'.",
                nameof(mode));
        }

        if (!FilterCompiler.TryCompile(comparisonText, out var compiled, out _)) { return null; }

        if (basicFilter is null &&
            mode == FilterMode.Basic &&
            BasicFilterDecomposer.TryDecompose(comparisonText, out var decomposed))
        {
            basicFilter = decomposed;
        }
        else if (mode != FilterMode.Basic)
        {
            basicFilter = null;
        }

        return new SavedFilter
        {
            Id = id ?? FilterId.Create(),
            Color = color,
            ComparisonText = comparisonText,
            Compiled = compiled,
            BasicFilter = basicFilter,
            IsEnabled = isEnabled,
            IsExcluded = isExcluded,
            Mode = mode
        };
    }

    public static SavedFilter LoadFromPersisted(
        string text,
        HighlightColor color,
        bool isExcluded,
        BasicFilter? persistedBasicFilter,
        FilterMode mode)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new SavedFilter
            {
                Color = color,
                ComparisonText = string.Empty,
                Compiled = null,
                BasicFilter = null,
                IsEnabled = false,
                IsExcluded = isExcluded,
                Mode = mode
            };
        }

        if (!FilterCompiler.TryCompile(text, out var compiled, out string? error))
        {
            Trace.TraceWarning(
                "SavedFilter: failed to compile persisted filter expression. Text='{0}', Error='{1}'",
                text,
                error);

            return new SavedFilter
            {
                Color = color,
                ComparisonText = text,
                Compiled = null,
                BasicFilter = null,
                IsEnabled = false,
                IsExcluded = isExcluded,
                Mode = mode
            };
        }

        BasicFilter? hydrated = null;

        if (mode == FilterMode.Basic)
        {
            // Try fresh decompose first (canonical, drift-resistant). When that refuses, fall back to the persisted
            // blob if the caller supplied one. This means hand-edited / partial-write JSON whose Mode=Basic but
            // BasicFilter blob is missing still gets a recovery attempt — without it the row would reopen with an
            // empty Basic editor while the raw text stays hidden, and the next save would silently wipe the text.
            if (BasicFilterDecomposer.TryDecompose(text, out var fresh))
            {
                hydrated = fresh;
            }
            else if (persistedBasicFilter is not null)
            {
                hydrated = persistedBasicFilter;
            }
        }

        return new SavedFilter
        {
            Color = color,
            ComparisonText = text,
            Compiled = compiled,
            BasicFilter = hydrated,
            IsEnabled = true,
            IsExcluded = isExcluded,
            Mode = mode
        };
    }
}
