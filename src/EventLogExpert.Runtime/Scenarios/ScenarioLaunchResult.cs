// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios;

public sealed record ScenarioLaunchResult(int Opened, int Empty, int Failed)
{
    public static ScenarioLaunchResult None { get; } = new(0, 0, 0) { ChannelOutcomes = [] };

    public bool AnyOpened => Opened > 0;

    public ImmutableArray<ChannelOutcome> ChannelOutcomes { get; init; } = [];
}
