// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;

namespace EventLogExpert.Eventing.Structured;

/// <summary>
///     Canonical UserData path grammar and extraction. A canonical path is a <c>/</c>-separated chain of element
///     local names, optionally ending in an <c>@attribute</c>, with <c>[*]</c> marking a repeating element. UserData
///     content inherits the event's default namespace with no explicit prefix, so matching is by local name only.
/// </summary>
internal static class StructuredFieldPath
{
    internal const int MaxWildcardValues = 256;

    private const string WildcardMarker = "[*]";

    private static readonly StructuredFieldResult s_absent =
        new(EventFieldValue.FromProperty(EventProperty.FromReference(null)), false);

    /// <summary>
    ///     Extracts every leaf value matching the element chain (a repeating element yields all same-name siblings) by
    ///     scanning <paramref name="xml" /> with a forward, DOM-free reader. Returns the values as a <c>StringArray</c>-kind
    ///     result, or an absent (Null) result when nothing matches; the result is flagged truncated when more than
    ///     <paramref name="cap" /> values were present.
    /// </summary>
    internal static StructuredFieldResult CollectValues(ReadOnlySpan<char> xml, string[] elements, string? attribute, int cap)
    {
        if (elements.Length == 0) { return s_absent; }

        List<string>? values = null;
        bool truncated = false;
        int depth = 0;
        int matched = 0;
        var scanner = new XmlSpanScanner(xml);

        while (scanner.Read())
        {
            if (scanner.NodeType == XmlSpanNode.EndElement)
            {
                if (depth > 0)
                {
                    depth--;

                    if (matched > depth) { matched = depth; }
                }

                continue;
            }

            bool matchesHere = matched == depth && matched < elements.Length && scanner.LocalName.SequenceEqual(elements[matched]);

            if (matchesHere && matched + 1 == elements.Length && TryReadLeaf(ref scanner, attribute, out string value))
            {
                if ((values ??= []).Count >= cap) { truncated = true; break; }

                values.Add(value);
            }

            if (!scanner.IsEmptyElement)
            {
                if (matchesHere) { matched++; }

                depth++;
            }
        }

        return values is null ?
            s_absent :
            new StructuredFieldResult(
                EventFieldValue.FromProperty(
                    EventProperty.FromReference(values.ToArray())),
                truncated);
    }

    /// <summary>Validates the canonical grammar so a malformed path is rejected before any use.</summary>
    internal static bool IsValidCanonical(string path)
    {
        if (string.IsNullOrEmpty(path)) { return false; }

        string[] segments = path.Split('/');

        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];

            if (index == segments.Length - 1 && segment.StartsWith('@'))
            {
                if (!IsValidName(segment[1..])) { return false; }

                continue;
            }

            string name = segment.EndsWith(WildcardMarker, StringComparison.Ordinal) ?
                segment[..^WildcardMarker.Length] : segment;

            if (!IsValidName(name)) { return false; }
        }

        return true;
    }

    internal static bool IsWildcard(string path) => path.Contains(WildcardMarker, StringComparison.Ordinal);

    /// <summary>Splits a path into its element local names (any <c>[*]</c> stripped) and the optional attribute leaf.</summary>
    internal static (string[] Elements, string? Attribute) Parse(string path)
    {
        string[] segments = path.Split('/');
        string? attribute = null;
        int elementCount = segments.Length;

        if (segments[^1].StartsWith('@'))
        {
            attribute = segments[^1][1..];
            elementCount--;
        }

        var elements = new string[elementCount];

        for (int index = 0; index < elementCount; index++)
        {
            string segment = segments[index];
            int marker = segment.IndexOf(WildcardMarker, StringComparison.Ordinal);
            elements[index] = marker >= 0 ? segment[..marker] : segment;
        }

        return (elements, attribute);
    }

    private static bool IsValidName(string name)
    {
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_')) { return false; }

        foreach (char character in name)
        {
            if (!(char.IsLetterOrDigit(character) || character is '_' or '-' or '.')) { return false; }
        }

        return true;
    }

    private static bool TryReadLeaf(ref XmlSpanScanner scanner, string? attribute, out string value)
    {
        if (attribute is null)
        {
            ReadOnlySpan<char> text = scanner.RawText(out bool isCData);
            value = isCData ? text.ToString() : XmlSpanScanner.DecodeToString(text);

            return true;
        }

        XmlAttributeLister attributes = scanner.Attributes;

        while (attributes.MoveNext())
        {
            if (attributes.LocalName.SequenceEqual(attribute))
            {
                value = XmlSpanScanner.DecodeToString(attributes.RawValue);

                return true;
            }
        }

        value = string.Empty;

        return false;
    }
}
