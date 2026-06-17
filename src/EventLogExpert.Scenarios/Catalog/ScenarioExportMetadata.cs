// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>Author-supplied scenario metadata for an export; null fields become editable TODO placeholders.</summary>
public sealed record ScenarioExportMetadata(
    string? Id,
    string? Name,
    string? Purpose,
    ScenarioGroup? Group,
    IReadOnlyList<string> Channels)
{
    public IReadOnlyList<string> Channels { get; init; } = Channels ?? throw new ArgumentNullException(nameof(Channels));
}
