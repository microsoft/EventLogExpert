// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Scenarios;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Menu;

public sealed record OpenLogsBatchResult(
    int Opened,
    int Empty,
    int Failed,
    int Skipped,
    ImmutableArray<string> EmptyNames)
{
    public static OpenLogsBatchResult None { get; } = new(0, 0, 0, 0, []) { ChannelOutcomes = [] };

    public bool AnyOpened => Opened > 0;

    public ImmutableArray<ChannelOutcome> ChannelOutcomes { get; init; } = [];
}
