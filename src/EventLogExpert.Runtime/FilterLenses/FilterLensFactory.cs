// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterLenses;

internal static class FilterLensFactory
{
    /// <summary>
    ///     Builds a lens that keeps only rows whose ActivityId equals <paramref name="activityId" />. An optional
    ///     <paramref name="label" /> overrides the default chip text - used by the parent-activity jump, which is an
    ///     ActivityId-equality narrowing surfaced to the user under a different name.
    /// </summary>
    public static FilterLens? ForActivityId(Guid activityId, string? originLog = null, string? label = null) =>
        BuildEqualityLens(EventProperty.ActivityId, activityId, label ?? $"Activity ID = {activityId}", originLog);

    /// <summary>
    ///     Builds a lens that keeps only rows whose RelatedActivityId equals <paramref name="relatedActivityId" />,
    ///     grouping events that share the same parent/correlation activity.
    /// </summary>
    public static FilterLens? ForRelatedActivityId(Guid relatedActivityId, string? originLog = null) =>
        BuildEqualityLens(
            EventProperty.RelatedActivityId,
            relatedActivityId,
            $"Related Activity ID = {relatedActivityId}",
            originLog);

    /// <summary>
    ///     Encodes a nullable-Guid equality narrowing (keep only rows where the field equals <paramref name="value" />)
    ///     as an <em>exclude-of-complement</em>: because the base's include filters are OR-combined an appended include would
    ///     broaden, so the complement (<c>field != value</c>, <see cref="SavedFilter.IsExcluded" />) is excluded to AND-narrow
    ///     to exactly <c>field == value</c>. <c>NotEqual</c> on a nullable-Guid column is total (a decisive Match for an
    ///     absent value), so the exclude hides absent-field rows rather than leaking them. Returns <see langword="null" />
    ///     only if the criterion fails to format or compile.
    /// </summary>
    private static FilterLens? BuildEqualityLens(EventProperty property, Guid value, string label, string? originLog)
    {
        if (!TryFormatNotEqual(property, value.ToString(), out var comparisonText))
        {
            return null;
        }

        var complement = SavedFilter.TryCreate(
            comparisonText,
            isExcluded: true,
            isEnabled: true,
            mode: FilterMode.Advanced);

        if (complement?.Compiled is null) { return null; }

        return new FilterLens
        {
            Label = label,
            Kind = LensKind.Property,
            ExcludeFilters = [complement],
            OriginLog = originLog
        };
    }

    private static bool TryFormatNotEqual(EventProperty property, string value, out string comparisonText)
    {
        var comparison = new FilterComparison
        {
            Property = property,
            Operator = ComparisonOperator.NotEqual,
            MatchMode = MatchMode.Single,
            Value = value
        };

        return BasicFilterFormatter.TryFormat(new BasicFilter(comparison, []), out comparisonText);
    }
}
