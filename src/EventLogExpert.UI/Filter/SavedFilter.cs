// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering;
using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Filter;

/// <summary>
///     Immutable saved filter. Carries <see cref="ComparisonText" /> together with the pre-compiled
///     <see cref="Compiled" /> predicate so consumers never have to recompile to evaluate. Basic filters additionally
///     retain <see cref="BasicFilter" /> for round-trip re-edit.
/// </summary>
[JsonConverter(typeof(SavedFilterJsonConverter))]
public sealed record SavedFilter
{
    /// <summary>Placeholder where a default-valued <see cref="SavedFilter" /> is required.</summary>
    public static readonly SavedFilter Empty = new() { ComparisonText = string.Empty, Compiled = null };

    [JsonIgnore]
    public FilterId Id { get; init; } = FilterId.Create();

    public HighlightColor Color { get; init; } = HighlightColor.None;

    /// <summary>The Dynamic-LINQ source expression. Empty when the filter is a placeholder.</summary>
    public required string ComparisonText { get; init; }

    /// <summary>
    ///     Compiled predicate + RequiresXml flag. <c>null</c> when <see cref="ComparisonText" /> is empty or when
    ///     persistence loaded a filter whose stored expression failed to compile.
    /// </summary>
    [JsonIgnore]
    public required CompiledFilter? Compiled { get; init; }

    /// <summary>
    ///     Structured form of Basic filters; persisted so re-edit reopens the original comparison + sub-filter structure.
    ///     <c>null</c> for filters that were authored as raw expressions.
    /// </summary>
    public BasicFilter? BasicFilter { get; init; }

    [JsonIgnore]
    public bool IsEnabled { get; init; }

    public bool IsExcluded { get; init; }

    /// <summary>
    ///     Compiles <paramref name="comparisonText" /> and returns a populated <see cref="SavedFilter" />, or <c>null</c>
    ///     if the expression fails to parse. <paramref name="basicFilter" /> is retained for round-trip re-edit of
    ///     structured filters; pass <c>null</c> for raw advanced/cached expressions.
    /// </summary>
    public static SavedFilter? TryCreate(
        string comparisonText,
        BasicFilter? basicFilter = null,
        HighlightColor color = HighlightColor.None,
        bool isExcluded = false,
        bool isEnabled = false,
        FilterId? id = null)
    {
        if (!FilterCompiler.TryCompile(comparisonText, out var compiled, out _)) { return null; }

        return new SavedFilter
        {
            Id = id ?? FilterId.Create(),
            Color = color,
            ComparisonText = comparisonText,
            Compiled = compiled,
            BasicFilter = basicFilter,
            IsEnabled = isEnabled,
            IsExcluded = isExcluded
        };
    }
}
