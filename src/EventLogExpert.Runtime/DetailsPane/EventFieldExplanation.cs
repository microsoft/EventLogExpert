// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DetailsPane;

public readonly record struct EventFieldExplanation(string? DecodedLabel, GlossaryTerm? Description)
{
    public bool HasValue => DecodedLabel is not null || Description is not null;
}
