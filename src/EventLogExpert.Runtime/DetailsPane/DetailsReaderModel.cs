// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.DetailsPane;

public sealed record DetailsReaderModel
{
    public required string EventId { get; init; }

    public required string Level { get; init; }

    public SeverityLevel? Severity { get; init; }

    public required IReadOnlyList<DetailsProperty> Header { get; init; }

    public required IReadOnlyList<DetailsProperty> SystemProperties { get; init; }

    public required IReadOnlyList<DetailsField> EventData { get; init; }

    public required IReadOnlyList<DetailsField> UserData { get; init; }

    public required string Message { get; init; }

    public bool HasMessage { get; init; }

    public bool UserDataIncomplete { get; init; }

    public bool HasNamedEventData => EventData.Count > 0;
}
