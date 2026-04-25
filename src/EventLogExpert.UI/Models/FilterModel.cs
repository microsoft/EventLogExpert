// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

/// <summary>
///     Immutable saved filter. Carries <see cref="ComparisonText" /> together with the pre-compiled
///     <see cref="Compiled" /> predicate so consumers never have to recompile to evaluate. Basic filters additionally
///     retain <see cref="BasicSource" /> for round-trip re-edit.
/// </summary>
[JsonConverter(typeof(FilterModelJsonConverter))]
public sealed record FilterModel
{
    /// <summary>Placeholder where a default-valued <see cref="FilterModel" /> is required.</summary>
    public static readonly FilterModel Empty = new() { ComparisonText = string.Empty, Compiled = null };

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
    ///     Structured source for Basic filters; persisted so re-edit reopens the original main + sub-clause structure.
    ///     <c>null</c> for Advanced and Cached filters.
    /// </summary>
    public BasicFilterSource? BasicSource { get; init; }

    public FilterType FilterType { get; init; } = FilterType.Advanced;

    [JsonIgnore]
    public bool IsEnabled { get; init; }

    public bool IsExcluded { get; init; }

    /// <summary>
    ///     Compiles <paramref name="comparisonText" /> and returns a populated <see cref="FilterModel" />, or <c>null</c>
    ///     if the expression fails to parse.
    /// </summary>
    public static FilterModel? TryCreate(
        string comparisonText,
        FilterType filterType = FilterType.Advanced,
        BasicFilterSource? basicSource = null,
        HighlightColor color = HighlightColor.None,
        bool isExcluded = false,
        bool isEnabled = false,
        FilterId? id = null)
    {
        if (!FilterCompiler.TryCompile(comparisonText, out var compiled, out _)) { return null; }

        return new FilterModel
        {
            Id = id ?? FilterId.Create(),
            Color = color,
            ComparisonText = comparisonText,
            Compiled = compiled,
            BasicSource = basicSource,
            FilterType = filterType,
            IsEnabled = isEnabled,
            IsExcluded = isExcluded
        };
    }
}
