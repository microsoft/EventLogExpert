// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering;
using EventLogExpert.Filtering.Persistence;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Filter;

/// <summary>
///     Immutable saved filter. Carries <see cref="ComparisonText" /> together with the pre-compiled
///     <see cref="Compiled" /> predicate so consumers never have to recompile to evaluate. <see cref="Mode" />
///     determines how the row is reopened for re-edit; Basic-mode filters additionally retain
///     <see cref="BasicFilter" /> for round-trip structure.
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
    ///     Structured form of Basic-mode filters; persisted so re-edit reopens the original comparison + sub-filter
    ///     structure. <c>null</c> for Advanced and Cached modes (preserved-as-text only).
    /// </summary>
    public BasicFilter? BasicFilter { get; init; }

    [JsonIgnore]
    public bool IsEnabled { get; init; }

    public bool IsExcluded { get; init; }

    /// <summary>
    ///     Authoring mode, persisted so re-edit reopens on the same surface (Basic structured editor / Advanced free-text
    ///     input / Cached inline picker). Defaults to <see cref="FilterMode.Advanced" /> for back-compat with legacy
    ///     persisted records that omitted the field; the converter infers the actual intent from
    ///     <see cref="BasicFilter" /> presence on legacy reads (see <see cref="SavedFilterJsonConverter" />).
    /// </summary>
    public FilterMode Mode { get; init; } = FilterMode.Advanced;

    /// <summary>
    ///     Compiles <paramref name="comparisonText" /> and returns a populated <see cref="SavedFilter" />, or <c>null</c>
    ///     if the expression fails to parse. <paramref name="mode" /> is authoritative — when
    ///     <paramref name="basicFilter" /> is supplied, <paramref name="mode" /> must be
    ///     <see cref="FilterMode.Basic" /> (a mismatch throws <see cref="ArgumentException" /> so caller errors fail
    ///     loudly at the call site rather than silently mutating the row's persisted authoring intent). When
    ///     <paramref name="mode" /> is <see cref="FilterMode.Basic" /> and no <paramref name="basicFilter" /> is
    ///     supplied, the factory opportunistically runs <see cref="BasicFilterDecomposer.TryDecompose" /> against the
    ///     compiled text and populates <see cref="BasicFilter" /> when the expression maps to the closed Basic
    ///     vocabulary. Advanced and Cached modes always force <see cref="BasicFilter" /> to <c>null</c> (preserves the
    ///     L3 "Advanced stays Advanced" intent guard explicitly).
    /// </summary>
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

        if (basicFilter is null && mode == FilterMode.Basic
            && BasicFilterDecomposer.TryDecompose(comparisonText, out var decomposed))
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

    /// <summary>
    ///     Reconstructs a <see cref="SavedFilter" /> from a persisted record. Always returns a value (never <c>null</c>) so
    ///     persistence corruption never blocks application start: invalid expressions surface as a disabled filter whose text
    ///     is preserved for the user to repair.
    ///     <para>
    ///         <paramref name="mode" /> is authoritative for <see cref="BasicFilter" /> hydration:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>
    ///                     <see cref="FilterMode.Advanced" /> or <see cref="FilterMode.Cached" /> → <see cref="BasicFilter" />
    ///                     is forced to <c>null</c> regardless of <paramref name="persistedBasicFilter" /> (preserves the L3
    ///                     "Advanced stays Advanced" intent guard explicitly; Cached rows always reopen with empty structure).
    ///                 </description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     <see cref="FilterMode.Basic" /> with <paramref name="persistedBasicFilter" /> <c>null</c> →
    ///                     run <see cref="BasicFilterDecomposer.TryDecompose" /> against <paramref name="text" /> to
    ///                     attempt structure recovery (covers hand-edited / partial-write JSON where the BasicFilter
    ///                     blob is missing). When the decomposer also refuses, leaves <see cref="BasicFilter" />
    ///                     <c>null</c>; the editor surfaces the row as empty Basic structure but preserves the raw
    ///                     <see cref="ComparisonText" /> so a subsequent mode switch to Advanced can recover it.
    ///                 </description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     <see cref="FilterMode.Basic" /> with non-<c>null</c> <paramref name="persistedBasicFilter" /> →
    ///                     run <see cref="BasicFilterDecomposer.TryDecompose" /> against <paramref name="text" />. When the
    ///                     decomposer succeeds, prefer the freshly-decomposed structure (canonical, drift-resistant,
    ///                     immune to formatter-version skew). When it refuses (e.g. older persisted vocabulary), fall back
    ///                     to <paramref name="persistedBasicFilter" /> so the structure isn't lost.
    ///                 </description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </summary>
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
