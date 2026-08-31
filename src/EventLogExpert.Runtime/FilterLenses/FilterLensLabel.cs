// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;

namespace EventLogExpert.Runtime.FilterLenses;

public abstract record FilterLensLabel
{
    private protected FilterLensLabel() { }

    public sealed record PropertyComparison(EventProperty Property, bool IsEqual, string Value) : FilterLensLabel;

    public sealed record ParentActivity(Guid ActivityId) : FilterLensLabel;

    public sealed record TimeRange(DateTime AfterLocal, DateTime BeforeLocal, bool SameDay) : FilterLensLabel;

    public sealed record TimeWindow(DateTime CenterLocal, TimeSpan Radius) : FilterLensLabel;
}
