// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Structured;

/// <summary>
///     Discovers the canonical UserData field paths across one or more event XML samples, for a field picker. A leaf
///     is a value-bearing attribute or a text-only element; an element that repeats (≥2 same-name siblings under one
///     parent) in any sample is emitted with a <c>[*]</c> marker so a single sample never hides multi-value content a
///     later event would show. The samples are scanned forward without building a DOM.
/// </summary>
public static class StructuredSchemaDiscovery
{
    private const string UserDataPrefix = "Event/UserData/";

    public static IReadOnlyList<string> DiscoverUserDataPaths(IEnumerable<string> eventXmlSamples)
    {
        ArgumentNullException.ThrowIfNull(eventXmlSamples);

        var samples = new List<string>();

        foreach (string xml in eventXmlSamples)
        {
            if (!string.IsNullOrEmpty(xml)) { samples.Add(xml); }
        }

        if (samples.Count == 0) { return []; }

        var repeating = new HashSet<string>(StringComparer.Ordinal);

        foreach (string xml in samples) { CollectRepeating(xml, repeating); }

        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (string xml in samples) { EmitPaths(xml, repeating, paths); }

        var ordered = new List<string>(paths);
        ordered.Sort(StringComparer.Ordinal);

        return ordered;
    }

    private static void CollectRepeating(string xml, HashSet<string> repeating)
    {
        var stack = new List<(string StructuralPath, Dictionary<string, int> ChildCounts)>();
        var scanner = new XmlSpanScanner(xml);

        while (scanner.Read())
        {
            if (scanner.NodeType == XmlSpanNode.EndElement)
            {
                if (stack.Count > 0) { stack.RemoveAt(stack.Count - 1); }

                continue;
            }

            string localName = scanner.LocalName.ToString();
            string structuralPath = stack.Count == 0 ? localName : stack[^1].StructuralPath + "/" + localName;

            if (stack.Count > 0)
            {
                Dictionary<string, int> childCounts = stack[^1].ChildCounts;
                int count = childCounts.GetValueOrDefault(localName) + 1;
                childCounts[localName] = count;

                if (count == 2) { repeating.Add(structuralPath); }
            }

            if (!scanner.IsEmptyElement)
            {
                stack.Add((structuralPath, new Dictionary<string, int>(StringComparer.Ordinal)));
            }
        }
    }

    private static void EmitPaths(string xml, HashSet<string> repeating, HashSet<string> paths)
    {
        var stack = new List<Frame>();
        var scanner = new XmlSpanScanner(xml);

        while (scanner.Read())
        {
            if (scanner.NodeType == XmlSpanNode.EndElement)
            {
                if (stack.Count == 0) { continue; }

                Frame closed = stack[^1];
                stack.RemoveAt(stack.Count - 1);

                if (closed is { HasChild: false, Text.Length: > 0 } &&
                    closed.CanonicalPath.StartsWith(UserDataPrefix, StringComparison.Ordinal))
                {
                    paths.Add(closed.CanonicalPath);
                }

                continue;
            }

            string localName = scanner.LocalName.ToString();
            Frame? parent = stack.Count > 0 ? stack[^1] : null;
            string structuralPath = parent is null ? localName : parent.StructuralPath + "/" + localName;
            string segment = repeating.Contains(structuralPath) ? localName + "[*]" : localName;
            string canonicalPath = parent is null ? segment : parent.CanonicalPath + "/" + segment;

            parent?.HasChild = true;

            if (canonicalPath.StartsWith(UserDataPrefix, StringComparison.Ordinal))
            {
                XmlAttributeLister attributes = scanner.Attributes;

                while (attributes.MoveNext())
                {
                    paths.Add(canonicalPath + "/@" + attributes.LocalName.ToString());
                }
            }

            if (scanner.IsEmptyElement) { continue; }

            ReadOnlySpan<char> text = scanner.RawText(out _);

            stack.Add(new Frame
            {
                CanonicalPath = canonicalPath,
                StructuralPath = structuralPath,
                Text = text.IsWhiteSpace() ? string.Empty : text.ToString()
            });
        }
    }

    private sealed class Frame
    {
        public required string CanonicalPath { get; init; }

        public bool HasChild { get; set; }

        public required string StructuralPath { get; init; }

        public required string Text { get; init; }
    }
}
