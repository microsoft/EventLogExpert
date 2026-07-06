// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Structured;

/// <summary>
///     Extracts every nested-UserData value from one event's rendered XML in a single forward, DOM-free scan. Each
///     field's path is a storage key (<see cref="StructuredFieldPath.ToStorageKey" /> form: the plain local-name chain
///     under <c>UserData</c>, <c>/@attr</c> for an attribute); repeats of a path collapse into one multi-value field.
/// </summary>
public static class UserDataValueExtractor
{
    /// <summary>
    ///     Most distinct paths kept per event; on overflow, new paths are dropped and the result flagged incomplete
    ///     (tracked paths keep collecting), bounding the stored set for a pathological event.
    /// </summary>
    internal const int MaxDistinctPathsPerEvent = 1024;

    /// <summary>
    ///     Most characters kept per value; a longer value is truncated and its field flagged, so a compare against the
    ///     full value reads the truncated value as <c>Unknown</c> (kept visible) rather than a wrong no-match.
    /// </summary>
    internal const int MaxValueChars = 4096;

    private const string UserDataEnvelopePrefix = "Event/UserData/";

    /// <summary>
    ///     Scans <paramref name="xml" /> once, returning the deduped UserData values and a flag set when the
    ///     distinct-path cap dropped paths (a not-found path is then ambiguous). Null/empty xml is decisively absent.
    /// </summary>
    public static (ImmutableArray<UserDataField> Fields, bool Incomplete) Extract(string? xml) =>
        Extract(xml, MaxDistinctPathsPerEvent);

    /// <summary>Test seam letting a test drive the distinct-path cap without a thousand-path document.</summary>
    internal static (ImmutableArray<UserDataField> Fields, bool Incomplete) Extract(string? xml, int maxDistinctPaths)
    {
        if (string.IsNullOrEmpty(xml)) { return (ImmutableArray<UserDataField>.Empty, false); }

        var fields = new List<MutableField>();
        var indexByPath = new Dictionary<string, int>(StringComparer.Ordinal);
        bool incomplete = false;

        var stack = new List<Frame>();
        var scanner = new XmlSpanScanner(xml);

        // xml is always a complete, well-formed RenderEventXml document, so the scan never terminates early.
        while (scanner.Read())
        {
            if (scanner.NodeType == XmlSpanNode.EndElement)
            {
                if (stack.Count == 0) { continue; }

                Frame closed = stack[^1];
                stack.RemoveAt(stack.Count - 1);

                if (closed is { HasChild: false, StorageKey: { Length: > 0 } textKey, Text.Length: > 0 })
                {
                    AddValue(textKey, closed.Text);
                }

                continue;
            }

            string localName = scanner.LocalName.ToString();
            Frame? parent = stack.Count > 0 ? stack[^1] : null;
            string structuralPath = parent is null ? localName : parent.StructuralPath + "/" + localName;
            string? storageKey = StorageKeyFor(structuralPath);

            parent?.HasChild = true;

            if (storageKey is { Length: > 0 })
            {
                XmlAttributeLister attributes = scanner.Attributes;

                while (attributes.MoveNext())
                {
                    AddValue(storageKey + "/@" + attributes.LocalName.ToString(), XmlSpanScanner.DecodeToString(attributes.RawValue));
                }
            }

            if (scanner.IsEmptyElement) { continue; }

            ReadOnlySpan<char> text = scanner.RawText(out bool isCData);
            string content = text.IsWhiteSpace()
                ? string.Empty
                : isCData ? text.ToString() : XmlSpanScanner.DecodeToString(text);

            stack.Add(new Frame
            {
                StorageKey = storageKey,
                StructuralPath = structuralPath,
                Text = content
            });
        }

        if (fields.Count == 0) { return (ImmutableArray<UserDataField>.Empty, incomplete); }

        ImmutableArray<UserDataField>.Builder builder = ImmutableArray.CreateBuilder<UserDataField>(fields.Count);

        foreach (MutableField field in fields)
        {
            builder.Add(new UserDataField(field.Path, [.. field.Values], field.IsTruncated));
        }

        return (builder.MoveToImmutable(), incomplete);

        void AddValue(string path, string value)
        {
            bool valueTruncated = value.Length > MaxValueChars;

            if (valueTruncated) { value = value[..MaxValueChars]; }

            if (indexByPath.TryGetValue(path, out int existing))
            {
                MutableField tracked = fields[existing];

                if (valueTruncated) { tracked.IsTruncated = true; }

                if (tracked.Values.Count >= StructuredFieldPath.MaxWildcardValues)
                {
                    tracked.IsTruncated = true;

                    return;
                }

                tracked.Values.Add(value);

                return;
            }

            if (fields.Count >= maxDistinctPaths)
            {
                incomplete = true;

                return;
            }

            MutableField created = new(path) { IsTruncated = valueTruncated };
            created.Values.Add(value);
            indexByPath[path] = fields.Count;
            fields.Add(created);
        }
    }

    private static string? StorageKeyFor(string structuralPath) =>
        structuralPath.StartsWith(UserDataEnvelopePrefix, StringComparison.Ordinal)
            ? structuralPath[UserDataEnvelopePrefix.Length..]
            : null;

    private sealed class Frame
    {
        public bool HasChild { get; set; }

        public required string? StorageKey { get; init; }

        public required string StructuralPath { get; init; }

        public required string Text { get; init; }
    }

    private sealed class MutableField(string path)
    {
        public bool IsTruncated { get; set; }

        public string Path { get; } = path;

        public List<string> Values { get; } = [];
    }
}
