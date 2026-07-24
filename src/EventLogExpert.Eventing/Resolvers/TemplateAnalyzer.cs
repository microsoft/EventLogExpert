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
    private static readonly TemplateInfo s_empty = new(
        new TemplateMetadata(
            0,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty),
        TemplateFieldSchema.Empty);

    private readonly ConcurrentDictionary<string, TemplateInfo> _cache =
        new(StringComparer.Ordinal);

    public TemplateMetadata Analyze(ReadOnlySpan<char> template) => GetTemplateInfo(template).Metadata;

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

    public TemplateInfo GetTemplateInfo(ReadOnlySpan<char> template)
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

        TemplateInfo info = Parse(template);

        // Publish to cache only after parsing completes so readers never observe partial metadata.
        lookup.TryAdd(template, info);

        return info;
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

    // Windows synthesizes "%1".."%N" <data> names for classic positional insertion strings; no real names exist
    // and Event Viewer shows those fields positionally. Only a template whose EVERY node is such a placeholder is
    // treated as classic and relabeled "Parameter N" (in BuildInfo); any real name or unnamed/raw node leaves it
    // untouched (fail closed).
    private static bool AllFieldNamesSynthetic(List<(string name, string outType, string map)> elements)
    {
        bool anySynthetic = false;

        foreach (var (name, _, _) in elements)
        {
            if (!IsSyntheticFieldName(name)) { return false; }

            anySynthetic = true;
        }

        return anySynthetic;
    }

    private static TemplateInfo BuildInfo(
        List<(string name, string outType, string map)> elements,
        HashSet<string> lengthProviderNames)
    {
        bool normalize = AllFieldNamesSynthetic(elements);

        var allNamesArray = new string[elements.Count];
        var allOutTypesArray = new string[elements.Count];
        var allMapsArray = new string[elements.Count];

        for (int i = 0; i < elements.Count; i++)
        {
            allNamesArray[i] = NormalizedName(elements[i].name, normalize);
            allOutTypesArray[i] = elements[i].outType;
            allMapsArray[i] = elements[i].map;
        }

        // ImmutableArray safely takes over each fresh local array without copying.
        var allNames = ImmutableCollectionsMarshal.AsImmutableArray(allNamesArray);
        var allOutTypes = ImmutableCollectionsMarshal.AsImmutableArray(allOutTypesArray);
        var allMaps = ImmutableCollectionsMarshal.AsImmutableArray(allMapsArray);

        if (lengthProviderNames.Count == 0)
        {
            var metadata = new TemplateMetadata(elements.Count, allOutTypes, allOutTypes, allMaps, allMaps);

            return new TemplateInfo(metadata, new TemplateFieldSchema(allNames, allNames));
        }

        int visibleCount = 0;

        foreach (var (name, _, _) in elements)
        {
            if (string.IsNullOrEmpty(name) || !lengthProviderNames.Contains(name))
            {
                visibleCount++;
            }
        }

        var visibleNamesArray = new string[visibleCount];
        var visibleOutTypesArray = new string[visibleCount];
        var visibleMapsArray = new string[visibleCount];
        int write = 0;

        for (int i = 0; i < elements.Count; i++)
        {
            var (name, outType, map) = elements[i];

            if (string.IsNullOrEmpty(name) || !lengthProviderNames.Contains(name))
            {
                visibleNamesArray[write] = allNamesArray[i];
                visibleOutTypesArray[write] = outType;
                visibleMapsArray[write] = map;
                write++;
            }
        }

        var visibleNames = ImmutableCollectionsMarshal.AsImmutableArray(visibleNamesArray);
        var visibleOutTypes = ImmutableCollectionsMarshal.AsImmutableArray(visibleOutTypesArray);
        var visibleMaps = ImmutableCollectionsMarshal.AsImmutableArray(visibleMapsArray);

        var visibleMetadata = new TemplateMetadata(visibleCount, allOutTypes, visibleOutTypes, allMaps, visibleMaps);

        return new TemplateInfo(visibleMetadata, new TemplateFieldSchema(allNames, visibleNames));
    }

    private static string FormatSyntheticFieldName(string name) => string.Concat("Parameter ", name.AsSpan(1));

    // Canonical positive ordinal: '%' + a leading-zero-free ASCII-digit run ("%1".."%N", not "%0"/"%01"). ASCII
    // range checks (not char.IsDigit) keep Unicode digits out of both the gate and the label.
    private static bool IsSyntheticFieldName(string name)
    {
        if (name.Length < 2 || name[0] != '%' || name[1] is < '1' or > '9') { return false; }

        for (int i = 2; i < name.Length; i++)
        {
            if (name[i] is < '0' or > '9') { return false; }
        }

        return true;
    }

    private static string NormalizedName(string name, bool normalize) =>
        normalize && IsSyntheticFieldName(name) ? FormatSyntheticFieldName(name) : name;

    private static TemplateInfo Parse(ReadOnlySpan<char> template)
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

        return BuildInfo(elements, lengthProviderNames);
    }
}
