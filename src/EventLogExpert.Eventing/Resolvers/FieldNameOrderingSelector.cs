// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Resolvers;

internal enum FieldNameOrdering : byte
{
    Visible,
    All
}

// Reproduces DescriptionFormatter's runtime length-match (Visible ordering first, then All) so field labeling and
// description formatting resolve the same ordering; returns false (fail closed) when the value count matches neither,
// which the accessor surfaces as EventDataKind.None rather than mislabeling.
internal static class FieldNameOrderingSelector
{
    public static bool TrySelectOrdering(in TemplateMetadata meta, int valueCount, out FieldNameOrdering ordering)
    {
        if (!meta.VisibleOutTypes.IsDefault && meta.VisibleOutTypes.Length == valueCount)
        {
            ordering = FieldNameOrdering.Visible;

            return true;
        }

        if (!meta.AllOutTypes.IsDefault && meta.AllOutTypes.Length == valueCount)
        {
            ordering = FieldNameOrdering.All;

            return true;
        }

        ordering = default;

        return false;
    }
}
