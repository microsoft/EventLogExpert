// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;

namespace EventLogExpert.Runtime.FilterLenses;

public abstract record FilterLensLabel
{
    private protected FilterLensLabel() { }

    public sealed record PropertyComparison(EventProperty Property, bool IsEqual, string Value) : FilterLensLabel;

    public sealed record ParentActivity(Guid ActivityId) : FilterLensLabel;

    /// <summary>UTC bounds; formatted in the display zone at render time so a zone change re-labels the chip.</summary>
    public sealed record TimeRange(DateTime AfterUtc, DateTime BeforeUtc) : FilterLensLabel;

    /// <summary>UTC center; formatted in the display zone at render time so a zone change re-labels the chip.</summary>
    public sealed record TimeWindow(DateTime CenterUtc, TimeSpan Radius) : FilterLensLabel;
}
