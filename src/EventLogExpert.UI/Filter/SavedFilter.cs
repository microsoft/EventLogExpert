// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering;
using System.Diagnostics;
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
    ///     if the expression fails to parse. When <paramref name="basicFilter" /> is supplied it is retained verbatim
    ///     (caller-provided structure always wins). When <paramref name="basicFilter" /> is <c>null</c> the factory
    ///     opportunistically runs <see cref="BasicFilterDecomposer.TryDecompose" /> against the compiled text and populates
    ///     <see cref="BasicFilter" /> when the expression maps to the closed Basic vocabulary; non-decomposable text leaves
    ///     <see cref="BasicFilter" /> <c>null</c> (raw / Advanced shape preserved). Callers that intentionally want the raw
    ///     shape regardless of decomposability — e.g. the Advanced-row save path in <c>EditableFilterRowBase.TrySaveAsync</c>
    ///     — must construct <see cref="SavedFilter" /> directly rather than route through this factory.
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

        if (basicFilter is null && BasicFilterDecomposer.TryDecompose(comparisonText, out var decomposed))
        {
            basicFilter = decomposed;
        }

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

    /// <summary>
    ///     Reconstructs a <see cref="SavedFilter" /> from a persisted record. Always returns a value (never <c>null</c>) so
    ///     persistence corruption never blocks application start: invalid expressions surface as a disabled filter whose text
    ///     is preserved for the user to repair.
    ///     <para>
    ///         Hydration policy for <paramref name="persistedBasicFilter" /> when <paramref name="text" /> compiles:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>
    ///                     <c>null</c> → <see cref="BasicFilter" /> stays <c>null</c>. The caller (typically the JSON
    ///                     converter) is responsible for distinguishing "no structured signal on disk" (preserve null intent —
    ///                     Advanced filters stay Advanced even when their text happens to decompose) from "structured signal
    ///                     present but blob missing" (caller pre-decomposes for the repair path).
    ///                 </description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     not <c>null</c> → run <see cref="BasicFilterDecomposer.TryDecompose" /> against
    ///                     <paramref name="text" />. When the decomposer succeeds, prefer the freshly-decomposed structure
    ///                     (canonical, drift-resistant, immune to formatter-version skew). When it refuses (e.g. older
    ///                     persisted vocabulary), fall back to <paramref name="persistedBasicFilter" /> so the structure isn't
    ///                     lost.
    ///                 </description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </summary>
    public static SavedFilter LoadFromPersisted(
        string text,
        HighlightColor color,
        bool isExcluded,
        BasicFilter? persistedBasicFilter)
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
                IsExcluded = isExcluded
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
                IsExcluded = isExcluded
            };
        }

        BasicFilter? hydrated = null;

        if (persistedBasicFilter is not null)
        {
            hydrated = BasicFilterDecomposer.TryDecompose(text, out var fresh)
                ? fresh
                : persistedBasicFilter;
        }

        return new SavedFilter
        {
            Color = color,
            ComparisonText = text,
            Compiled = compiled,
            BasicFilter = hydrated,
            IsEnabled = true,
            IsExcluded = isExcluded
        };
    }
}
