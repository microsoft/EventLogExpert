// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderMetadata.Wevt;
using EventLogExpert.Provider.Resolution;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Resolvers;

// Length-provider <data> nodes are consumed by EvtRender, so visible metadata excludes referenced length fields.
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

        // Publish to cache only after parsing completes so readers never observe partial metadata.
        lookup.TryAdd(template, metadata);

        return metadata;
    }

    // Allows one extra template node for newer manifest versions with an added optional field.
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

    // Accept full or visible counts because providers vary on length-provider field output.
    public bool MatchesPropertyCount(ReadOnlySpan<char> template, int eventPropertyCount)
    {
        if (template.IsEmpty) { return false; }

        var metadata = Analyze(template);

        if (metadata.AllOutTypes.Length == eventPropertyCount) { return true; }

        return metadata.VisiblePropertyCount == eventPropertyCount;
    }

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

        // ImmutableArray safely takes over this fresh local array without copying.
        var allOutTypes = ImmutableCollectionsMarshal.AsImmutableArray(allOutTypesArray);
        var allMaps = ImmutableCollectionsMarshal.AsImmutableArray(allMapsArray);

        if (lengthProviderNames.Count == 0)
        {
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
                // Non-canonical elements remain visible but carry no outType/map, matching the prior scanner.
                elements.Add((string.Empty, string.Empty, string.Empty));

                continue;
            }

            elements.Add((
                field.Name.IsEmpty ? string.Empty : WevtTemplateWriter.UnescapeXmlAttribute(field.Name),
                field.OutType.IsEmpty ? string.Empty : WevtTemplateWriter.UnescapeXmlAttribute(field.OutType),
                field.Map.IsEmpty ? string.Empty : WevtTemplateWriter.UnescapeXmlAttribute(field.Map)));

            if (!field.Length.IsEmpty)
            {
                lengthProviderNames.Add(WevtTemplateWriter.UnescapeXmlAttribute(field.Length));
            }
        }

        return BuildMetadata(elements, lengthProviderNames);
    }
}
