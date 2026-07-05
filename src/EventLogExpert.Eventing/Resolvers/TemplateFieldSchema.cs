// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Resolvers;

// The per-template metadata (out-types/maps) plus the reference-shared field-name schema, cached once per template
// string and threaded from the resolve pass into both DescriptionFormatter and EventData population.
internal readonly record struct TemplateInfo(TemplateMetadata Metadata, TemplateFieldSchema Schema);

/// <summary>
///     Reference-shared per-template field names in both orderings (all &lt;data&gt; nodes, and the visible subset
///     that excludes length-provider nodes), positionally aligned with the matching <see cref="TemplateMetadata" />
///     out-type arrays. The name-to-index maps are built lazily on first lookup and published atomically.
/// </summary>
internal sealed class TemplateFieldSchema(ImmutableArray<string> allNames, ImmutableArray<string> visibleNames)
{
    public static readonly TemplateFieldSchema Empty = new(ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);

    private FrozenDictionary<string, int>? _allIndex;
    private FrozenDictionary<string, int>? _visibleIndex;

    public ImmutableArray<string> AllNames { get; } = allNames;

    public ImmutableArray<string> VisibleNames { get; } = visibleNames;

    public bool TryGetIndex(FieldNameOrdering ordering, string name, out int index)
    {
        FrozenDictionary<string, int> map = ordering == FieldNameOrdering.Visible
            ? GetOrBuildIndex(ref _visibleIndex, VisibleNames)
            : GetOrBuildIndex(ref _allIndex, AllNames);

        return map.TryGetValue(name, out index);
    }

    private static FrozenDictionary<string, int> BuildIndex(ImmutableArray<string> names)
    {
        var map = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);

        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];

            // Empty-name (raw/non-canonical) nodes are positional-only; a duplicate name keeps its first index.
            if (!string.IsNullOrEmpty(name)) { map.TryAdd(name, i); }
        }

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, int> GetOrBuildIndex(ref FrozenDictionary<string, int>? slot, ImmutableArray<string> names)
    {
        FrozenDictionary<string, int>? existing = Volatile.Read(ref slot);

        if (existing is not null) { return existing; }

        FrozenDictionary<string, int> built = BuildIndex(names);

        // First writer wins; a racing reader either sees null (and builds its own discarded copy) or a complete map.
        return Interlocked.CompareExchange(ref slot, built, null) ?? built;
    }
}
