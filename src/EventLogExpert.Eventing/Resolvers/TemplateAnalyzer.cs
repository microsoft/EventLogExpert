// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Resolvers;

/// <summary>
///     Unified parser for Windows event manifest &lt;template&gt; XML. Replaces three independently-cached parsers
///     (per-element name extraction, per-element outType extraction, visible-property count) with a single parse pass that
///     produces a <see cref="TemplateMetadata" /> value cached per template string.
/// </summary>
/// <remarks>
///     <para>
///         A "visible" property is a &lt;data&gt; element Windows surfaces through EvtRender. Length-provider elements —
///         &lt;data&gt; elements whose <c>name</c> is referenced by another element's <c>length</c> attribute — are
///         consumed by Windows internally and do not appear as separate user properties. <see cref="Analyze" /> filters
///         them out of <see cref="TemplateMetadata.VisibleOutTypes" /> and excludes them from
///         <see cref="TemplateMetadata.VisiblePropertyCount" />.
///     </para>
///     <para>
///         The cache is keyed by the template string but supports zero-allocation lookup from a
///         <see cref="ReadOnlySpan{Char}" /> via <see cref="ConcurrentDictionary{TKey,TValue}.GetAlternateLookup" />.
///     </para>
/// </remarks>
internal sealed class TemplateAnalyzer
{
    private static readonly TemplateMetadata s_empty = new(
        0,
        ImmutableArray<string>.Empty,
        ImmutableArray<string>.Empty,
        ImmutableArray<string>.Empty,
        ImmutableArray<string>.Empty);

    private readonly ConcurrentDictionary<string, TemplateMetadata> _cache =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Parses <paramref name="template" /> if it is not already cached and returns its
    ///     <see cref="TemplateMetadata" />. Empty templates return empty metadata without touching the cache.
    /// </summary>
    public TemplateMetadata Analyze(ReadOnlySpan<char> template)
    {
        if (template.IsEmpty)
        {
            return s_empty;
        }

        var lookup = _cache.GetAlternateLookup<ReadOnlySpan<char>>();

        if (lookup.TryGetValue(template, out var cached))
        {
            return cached;
        }

        TemplateMetadata metadata = Parse(template);

        // Cache write happens AFTER the parse loop completes; partially-built metadata is never visible
        // to other readers (§3.8 deferred-mutations).
        lookup.TryAdd(template, metadata);

        return metadata;
    }

    /// <summary>
    ///     Relaxed match for exact Id+Version+LogName candidates — accepts the template having exactly one MORE data node
    ///     than the event has properties. Handles version mismatches where the manifest added an optional field in a newer
    ///     version.
    /// </summary>
    /// <remarks>
    ///     Uses sequential fallback — visible-only count first, falling back to the all-node count ONLY when the visible
    ///     count is zero.
    /// </remarks>
    public bool ApproximatelyMatchesPropertyCount(ReadOnlySpan<char> template, int eventPropertyCount)
    {
        if (template.IsEmpty || eventPropertyCount <= 0) { return false; }

        var metadata = Analyze(template);

        int templateCount = metadata.VisiblePropertyCount;

        if (templateCount == 0)
        {
            templateCount = metadata.AllOutTypes.Length;
        }

        return templateCount - eventPropertyCount == 1;
    }

    /// <summary>
    ///     Returns true when the template has the same number of data nodes as the event has properties. Either the full
    ///     count or the visible-only count is accepted to handle providers that include or omit length-provider fields in
    ///     their property output.
    /// </summary>
    /// <remarks>
    ///     Accepts either the full data-node count or the visible-only count, since EvtRender output may include or omit
    ///     length-provider fields depending on provider.
    /// </remarks>
    public bool MatchesPropertyCount(ReadOnlySpan<char> template, int eventPropertyCount)
    {
        if (template.IsEmpty) { return false; }

        var metadata = Analyze(template);

        if (metadata.AllOutTypes.Length == eventPropertyCount) { return true; }

        return metadata.VisiblePropertyCount == eventPropertyCount;
    }

    /// <summary>
    ///     Strict variant of <see cref="MatchesPropertyCount" /> — accepts an empty template only when the event has zero
    ///     properties (which is the shape some providers emit for full-RawId entries with no EventData section).
    /// </summary>
    /// <remarks>
    ///     Stricter than <see cref="MatchesPropertyCount" />: an empty template is accepted ONLY when the event has zero
    ///     properties (which is the shape some providers emit for full-RawId entries with no EventData section).
    /// </remarks>
    public bool StrictlyMatchesPropertyCount(ReadOnlySpan<char> template, int eventPropertyCount)
    {
        if (template.IsEmpty) { return eventPropertyCount == 0; }

        return MatchesPropertyCount(template, eventPropertyCount);
    }

    private static TemplateMetadata BuildMetadata(
        List<(string name, string outType, string map)> elements,
        HashSet<string> lengthProviderNames)
    {
        var allOutTypesArray = new string[elements.Count];
        var allMapsArray = new string[elements.Count];

        for (int i = 0; i < elements.Count; i++)
        {
            allOutTypesArray[i] = elements[i].outType;
            allMapsArray[i] = elements[i].map;
        }

        // Zero-alloc wrap: takes ownership of the existing array as an ImmutableArray.
        // Safe because allOutTypesArray is a fresh local with no other references.
        var allOutTypes = ImmutableCollectionsMarshal.AsImmutableArray(allOutTypesArray);
        var allMaps = ImmutableCollectionsMarshal.AsImmutableArray(allMapsArray);

        if (lengthProviderNames.Count == 0)
        {
            // No hidden length-provider elements - visible and all are identical.
            // ImmutableArray<string> is a struct wrapping the same backing array;
            // both fields share the wrap so consumers cannot mutate the cache.
            return new TemplateMetadata(elements.Count, allOutTypes, allOutTypes, allMaps, allMaps);
        }

        int visibleCount = 0;

        foreach (var (name, _, _) in elements)
        {
            if (string.IsNullOrEmpty(name) || !lengthProviderNames.Contains(name))
            {
                visibleCount++;
            }
        }

        var visibleOutTypesArray = new string[visibleCount];
        var visibleMapsArray = new string[visibleCount];
        int write = 0;

        foreach (var (name, outType, map) in elements)
        {
            if (string.IsNullOrEmpty(name) || !lengthProviderNames.Contains(name))
            {
                visibleOutTypesArray[write] = outType;
                visibleMapsArray[write] = map;
                write++;
            }
        }

        var visibleOutTypes = ImmutableCollectionsMarshal.AsImmutableArray(visibleOutTypesArray);
        var visibleMaps = ImmutableCollectionsMarshal.AsImmutableArray(visibleMapsArray);

        return new TemplateMetadata(visibleCount, allOutTypes, visibleOutTypes, allMaps, visibleMaps);
    }

    private static TemplateMetadata Parse(ReadOnlySpan<char> template)
    {
        List<(string name, string outType, string map)> elements = [];
        HashSet<string> lengthProviderNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (TemplateField field in new TemplateFieldReader(template))
        {
            if (field.IsRaw)
            {
                // A non-canonical element contributes a visible node with no outType/map (matching the prior scanner).
                elements.Add((string.Empty, string.Empty, string.Empty));

                continue;
            }

            elements.Add((
                field.Name.IsEmpty ? string.Empty : new string(field.Name),
                field.OutType.IsEmpty ? string.Empty : new string(field.OutType),
                field.Map.IsEmpty ? string.Empty : new string(field.Map)));

            if (!field.Length.IsEmpty)
            {
                lengthProviderNames.Add(new string(field.Length));
            }
        }

        return BuildMetadata(elements, lengthProviderNames);
    }
}
