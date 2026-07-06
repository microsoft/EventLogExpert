// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Structured;

/// <summary>
///     The value of one structured (UserData) field extracted from an event. A repeating field materializes as a
///     <c>StringArray</c>-kind <see cref="EventFieldValue" />; <see cref="IsTruncated" /> is set when a repeating field
///     had more values than the extraction cap, so the present values are kept and the loss is still visible.
/// </summary>
public readonly struct StructuredFieldResult(EventFieldValue value, bool isTruncated)
{
    public EventFieldValue Value { get; } = value;

    public bool IsTruncated { get; } = isTruncated;
}
