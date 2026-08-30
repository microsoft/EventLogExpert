// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.DetailsPane;

/// <summary>
///     A single label / value row in the reader view's identity header or system-details section.
///     <see cref="Label" /> is a typed identity the UI localizes and copy renders invariantly. <see cref="StatusValue" />
///     is set only on the resolution-status row, where the UI shows the localized status while copy uses the invariant
///     <see cref="Value" />.
/// </summary>
public readonly record struct DetailsProperty(DetailsPropertyLabel Label, string Value)
{
    public EventResolutionStatus? StatusValue { get; init; }
}
