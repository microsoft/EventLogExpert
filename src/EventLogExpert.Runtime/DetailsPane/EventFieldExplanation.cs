// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DetailsPane;

/// <summary>
///     An inline explanation for a single EventData field. <see cref="DecodedLabel" /> is a human reading of the
///     field's value (e.g. a logon type of <c>3</c> to "Network") shown beside the raw value and included in copy;
///     <see cref="Description" /> is glossary prose about what the field means, shown as a muted note and excluded from
///     copy. The two resolve independently, so a field may carry one, both, or neither.
/// </summary>
public readonly record struct EventFieldExplanation(string? DecodedLabel, string? Description)
{
    public bool HasValue => DecodedLabel is not null || Description is not null;
}
