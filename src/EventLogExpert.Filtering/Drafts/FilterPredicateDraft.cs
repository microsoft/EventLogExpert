// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Filtering.Drafts;

public sealed class FilterPredicateDraft
{
    public FilterComparisonDraft Comparison { get; set; } = new();

    public FilterId Id { get; init; } = FilterId.Create();

    /// <summary>
    ///     A predicate is complete when its <see cref="Comparison" /> has a non-empty value:
    ///     <list type="bullet">
    ///         <item><c>Single</c> match mode requires a non-whitespace <see cref="FilterComparisonDraft.Value" />.</item>
    ///         <item><c>Many</c> match mode requires at least one entry in <see cref="FilterComparisonDraft.Values" />.</item>
    ///         <item>
    ///             An <see cref="EventProperty.EventData" /> row additionally requires a non-whitespace
    ///             <see cref="FilterComparisonDraft.EventDataFieldName" />.
    ///         </item>
    ///         <item>
    ///             A <see cref="EventProperty.UserData" /> row additionally requires a non-whitespace
    ///             <see cref="FilterComparisonDraft.UserDataFieldName" />.
    ///         </item>
    ///     </list>
    ///     Mirrors the formatter's strict-mode validation so the in-flight UI state matches the eventual save guard.
    /// </summary>
    public bool IsComplete
    {
        get
        {
            if (Comparison.Property is EventProperty.EventData
                && string.IsNullOrWhiteSpace(Comparison.EventDataFieldName))
            {
                return false;
            }

            if (Comparison.Property is EventProperty.UserData
                && string.IsNullOrWhiteSpace(Comparison.UserDataFieldName))
            {
                return false;
            }

            return Comparison.MatchMode == MatchMode.Many
                ? Comparison.Values.Count > 0
                : !string.IsNullOrWhiteSpace(Comparison.Value);
        }
    }

    public bool JoinWithAny { get; set; }

    public FilterPredicate ToFilterPredicate() => new(Comparison.ToComparison(), JoinWithAny);
}
