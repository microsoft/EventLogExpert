// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DetailsPane;

public sealed record DetailsField
{
    public required string Label { get; init; }

    public required IReadOnlyList<string> PreviewLines { get; init; }

    public required IReadOnlyList<string> FullLines { get; init; }

    public required string CopyValue { get; init; }

    public bool IsTruncated { get; init; }

    public bool IsMuted { get; init; }

    public bool IsMonospace { get; init; }

    public string? DecodedLabel { get; init; }

    public string? Description { get; init; }

    public bool PreferFullWidth => PreviewLines.Count > 1 || IsTruncated || Description is not null;
}
