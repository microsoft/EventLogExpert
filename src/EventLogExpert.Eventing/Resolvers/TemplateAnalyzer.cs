// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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
    private static readonly TemplateMetadata s_empty = new(0, ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);

    private readonly ConcurrentDictionary<string, TemplateMetadata> _cache =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Parses <paramref name="template" /> if it is not already cached and returns its
    ///     <see cref="TemplateMetadata" />. Empty templates return <see cref="s_empty" /> without touching the cache.
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
    ///     Preserves the exact semantics of the original DoesTemplateApproximatelyMatchPropertyCount, including the
    ///     sequential fallback: visible-count first, falling back to all-node count ONLY when the visible count is zero.
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
    /// <remarks>Preserves the exact semantics of the original DoesTemplateMatchPropertyCount.</remarks>
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
    /// <remarks>Preserves the exact semantics of the original DoesTemplateStrictlyMatchPropertyCount.</remarks>
    public bool StrictlyMatchesPropertyCount(ReadOnlySpan<char> template, int eventPropertyCount)
    {
        if (template.IsEmpty) { return eventPropertyCount == 0; }

        return MatchesPropertyCount(template, eventPropertyCount);
    }

    private static TemplateMetadata BuildMetadata(
        List<(string name, string outType)> elements,
        HashSet<string> lengthProviderNames)
    {
        var allOutTypesArray = new string[elements.Count];

        for (int i = 0; i < elements.Count; i++)
        {
            allOutTypesArray[i] = elements[i].outType;
        }

        // Zero-alloc wrap: takes ownership of the existing array as an ImmutableArray.
        // Safe because allOutTypesArray is a fresh local with no other references.
        var allOutTypes = ImmutableCollectionsMarshal.AsImmutableArray(allOutTypesArray);

        if (lengthProviderNames.Count == 0)
        {
            // No hidden length-provider elements — visible and all are identical.
            // ImmutableArray<string> is a struct wrapping the same backing array;
            // both fields share the wrap so consumers cannot mutate the cache.
            return new TemplateMetadata(elements.Count, allOutTypes, allOutTypes);
        }

        int visibleCount = 0;

        foreach (var (name, _) in elements)
        {
            if (string.IsNullOrEmpty(name) || !lengthProviderNames.Contains(name))
            {
                visibleCount++;
            }
        }

        var visibleOutTypesArray = new string[visibleCount];
        int write = 0;

        foreach (var (name, outType) in elements)
        {
            if (string.IsNullOrEmpty(name) || !lengthProviderNames.Contains(name))
            {
                visibleOutTypesArray[write++] = outType;
            }
        }

        var visibleOutTypes = ImmutableCollectionsMarshal.AsImmutableArray(visibleOutTypesArray);

        return new TemplateMetadata(visibleCount, allOutTypes, visibleOutTypes);
    }

    private static string? ExtractAttribute(ReadOnlySpan<char> element, ReadOnlySpan<char> attributePrefix)
    {
        int index = element.IndexOf(attributePrefix, StringComparison.Ordinal);

        if (index == -1) { return null; }

        index += attributePrefix.Length;
        int endIndex = element[index..].IndexOf('"');

        return endIndex != -1 ? new string(element.Slice(index, endIndex)) : null;
    }

    private static TemplateMetadata Parse(ReadOnlySpan<char> template)
    {
        List<(string name, string outType)> elements = [];
        HashSet<string> lengthProviderNames = new(StringComparer.OrdinalIgnoreCase);

        ReadOnlySpan<char> dataTag = "<data";
        ReadOnlySpan<char> nameAttr = "name=\"";
        ReadOnlySpan<char> outTypeAttr = "outType=\"";
        ReadOnlySpan<char> lengthAttr = "length=\"";

        int searchStart = 0;

        while (searchStart < template.Length)
        {
            int dataIndex = template[searchStart..].IndexOf(dataTag, StringComparison.OrdinalIgnoreCase);

            if (dataIndex == -1) { break; }

            dataIndex += searchStart;

            // Verify the character after "<data" is whitespace, '/', or '>'
            // to avoid matching tags like "<dataSource"
            int nextCharIndex = dataIndex + dataTag.Length;

            if (nextCharIndex < template.Length)
            {
                char next = template[nextCharIndex];

                if (next != ' ' && next != '\t' && next != '\r' && next != '\n' && next != '/' && next != '>')
                {
                    searchStart = nextCharIndex;

                    continue;
                }
            }

            int elementEnd = template[dataIndex..].IndexOf("/>");

            if (elementEnd == -1)
            {
                elementEnd = template[dataIndex..].IndexOf('>');
            }

            if (elementEnd == -1) { break; }

            elementEnd += dataIndex;

            ReadOnlySpan<char> element = template[dataIndex..elementEnd];

            string name = ExtractAttribute(element, nameAttr) ?? string.Empty;
            string outType = ExtractAttribute(element, outTypeAttr) ?? string.Empty;
            elements.Add((name, outType));

            string? lengthRef = ExtractAttribute(element, lengthAttr);

            if (lengthRef is not null)
            {
                lengthProviderNames.Add(lengthRef);
            }

            searchStart = elementEnd + 1;
        }

        return BuildMetadata(elements, lengthProviderNames);
    }
}
