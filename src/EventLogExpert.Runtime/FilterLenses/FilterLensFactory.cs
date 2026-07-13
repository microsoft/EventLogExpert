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
    ///     Builds a "Show Related by Activity ID" lens: keep only rows whose ActivityId equals
    ///     <paramref name="activityId" />, hiding every other row including those with no ActivityId. Because the base's
    ///     include filters are OR-combined an appended include would broaden, so the complement is excluded (
    ///     <c>ActivityId != X</c>, <see cref="SavedFilter.IsExcluded" />) to AND-narrow to exactly <c>ActivityId == X</c>.
    ///     <c>NotEqual</c> on the nullable-Guid column is total (a decisive Match for an absent value), so the exclude hides
    ///     absent-ActivityId rows rather than leaking them. Returns <see langword="null" /> only if the criterion fails to
    ///     format or compile.
    /// </summary>
    public static FilterLens? ForActivityId(Guid activityId, string? originLog = null)
    {
        if (!TryFormatNotEqual(EventProperty.ActivityId, activityId.ToString(), out var comparisonText))
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
            Label = $"Activity ID = {activityId}",
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
