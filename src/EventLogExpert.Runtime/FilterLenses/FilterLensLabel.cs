// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;

namespace EventLogExpert.Runtime.FilterLenses;

public abstract record FilterLensLabel
{
    private protected FilterLensLabel() { }

    public sealed record PropertyComparison(EventProperty Property, bool IsEqual, string Value) : FilterLensLabel;

    public sealed record ParentActivity(Guid ActivityId) : FilterLensLabel;

    public sealed record TimeRange(DateTime AfterUtc, DateTime BeforeUtc) : FilterLensLabel;

    public sealed record TimeWindow(DateTime CenterUtc, TimeSpan Radius) : FilterLensLabel;
}
