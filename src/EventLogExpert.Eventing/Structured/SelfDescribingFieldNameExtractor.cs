// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Structured;

/// <summary>
///     Reads the ordered <c>&lt;Data Name='...'&gt;</c> field names from a self-describing (TraceLogging) event's
///     rendered XML in a single forward, DOM-free scan. The reader detects a self-describing event cheaply via a values
///     render of its inline <c>&lt;EventData Name='...'&gt;</c> name and only then renders XML to recover these labels;
///     the field values themselves are reused from the already-rendered event properties. Names come back in document
///     order, index-aligned to those values. An unnamed <c>&lt;Data&gt;</c> contributes an empty label.
/// </summary>
internal static class SelfDescribingFieldNameExtractor
{
    // Bounds the retained name set so a pathological event cannot allocate unbounded; extra fields are unlabeled.
    internal const int MaxFieldNames = 1024;

    public static ImmutableArray<string> Extract(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) { return ImmutableArray<string>.Empty; }

        var scanner = new XmlSpanScanner(xml);

        int depth = 0;
        bool insideEventData = false;
        int eventDataChildDepth = -1;
        List<string>? fieldNames = null;

        while (scanner.Read())
        {
            if (scanner.NodeType == XmlSpanNode.EndElement)
            {
                depth--;

                if (insideEventData && depth < eventDataChildDepth) { insideEventData = false; }

                continue;
            }

            bool isEmptyElement = scanner.IsEmptyElement;

            if (!insideEventData && depth == 1 && scanner.LocalName is "EventData")
            {
                insideEventData = true;
                eventDataChildDepth = depth + 1;
            }
            else if (insideEventData && depth == eventDataChildDepth && scanner.LocalName is "Data")
            {
                if (fieldNames is null || fieldNames.Count < MaxFieldNames)
                {
                    (fieldNames ??= []).Add(ReadNameAttribute(scanner) ?? string.Empty);
                }
            }

            if (!isEmptyElement) { depth++; }
        }

        return fieldNames is null ? ImmutableArray<string>.Empty : [.. fieldNames];
    }

    private static string? ReadNameAttribute(XmlSpanScanner scanner)
    {
        XmlAttributeLister attributes = scanner.Attributes;

        while (attributes.MoveNext())
        {
            if (attributes.LocalName is "Name")
            {
                return XmlSpanScanner.DecodeToString(attributes.RawValue);
            }
        }

        return null;
    }
}
