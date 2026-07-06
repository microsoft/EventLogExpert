// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace EventLogExpert.Filtering.Basic;

/// <summary>Immutable representation of a single Basic-filter criterion (one row of the editor).</summary>
[JsonConverter(typeof(FilterComparisonJsonConverter))]
public sealed record FilterComparison
{
    public EventProperty Property { get; init; } = EventProperty.Id;

    public ComparisonOperator Operator { get; init; }

    public MatchMode MatchMode { get; init; }

    public string? Value { get; init; }

    public ImmutableList<string> Values { get; init; } = [];

    /// <summary>
    ///     The named &lt;EventData&gt; field this row targets. Meaningful only when <see cref="Property" /> is
    ///     <see cref="EventProperty.EventData" /> (null for every other property).
    /// </summary>
    public string? EventDataFieldName { get; init; }

    /// <summary>
    ///     The structured &lt;UserData&gt; path (storage-key form, e.g. <c>X509Objects/Certificate/SubjectName</c>) this
    ///     row targets. Meaningful only when <see cref="Property" /> is <see cref="EventProperty.UserData" />, else null.
    /// </summary>
    public string? UserDataFieldName { get; init; }

    /// <summary>
    ///     Returns a copy with the new <paramref name="property" /> and Value/Values/EventDataFieldName/UserDataFieldName
    ///     cleared, since the available value space (and whether a field name applies) changes when the property changes.
    /// </summary>
    public FilterComparison WithProperty(EventProperty property) =>
        this with { Property = property, Value = null, Values = [], EventDataFieldName = null, UserDataFieldName = null };
}
