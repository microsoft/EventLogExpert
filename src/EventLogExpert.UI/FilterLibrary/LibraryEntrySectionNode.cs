// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLibrary;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterLibrary;

public sealed record LibraryEntrySectionNode(
    string SectionName,
    ImmutableList<LibraryEntry> DirectEntries,
    ImmutableList<LibraryEntrySectionNode> ChildSections)
{
    internal const char PathDelimiter = '\\';

    public static IReadOnlyList<LibraryEntrySectionNode> BuildTree(IReadOnlyList<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var rootSections = new SortedDictionary<string, MutableNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var segments = SanitizeSegments(entry.Name);
            if (segments.Length <= 1) { continue; }

            InsertIntoTree(rootSections, segments, entry);
        }

        return MaterializeNodes(rootSections);
    }

    public static IReadOnlyList<LibraryEntry> BuildFlatRootEntries(IReadOnlyList<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var result = new List<LibraryEntry>();

        foreach (var entry in entries)
        {
            var segments = SanitizeSegments(entry.Name);
            if (segments.Length <= 1) { result.Add(entry); }
        }

        return result;
    }

    private static string[] SanitizeSegments(string name)
    {
        var raw = name.Split(PathDelimiter, StringSplitOptions.RemoveEmptyEntries);
        return raw;
    }

    private static void InsertIntoTree(SortedDictionary<string, MutableNode> roots, string[] segments, LibraryEntry entry)
    {
        var current = roots;
        MutableNode? node = null;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            var sectionName = segments[i];
            if (!current.TryGetValue(sectionName, out node))
            {
                node = new MutableNode(sectionName);
                current[sectionName] = node;
            }
            current = node.ChildSections;
        }

        node!.DirectEntries.Add(entry);
    }

    private static IReadOnlyList<LibraryEntrySectionNode> MaterializeNodes(SortedDictionary<string, MutableNode> nodes)
    {
        var result = new List<LibraryEntrySectionNode>(nodes.Count);

        foreach (var (_, mutable) in nodes)
        {
            result.Add(new LibraryEntrySectionNode(
                mutable.SectionName,
                [.. mutable.DirectEntries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)],
                [.. MaterializeNodes(mutable.ChildSections)]));
        }

        return result;
    }

    private sealed class MutableNode(string sectionName)
    {
        public SortedDictionary<string, MutableNode> ChildSections { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<LibraryEntry> DirectEntries { get; } = [];

        public string SectionName { get; } = sectionName;
    }
}
