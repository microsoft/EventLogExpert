// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>Outcome of launching a scenario: how its channels opened.</summary>
public sealed record ScenarioLaunchResult(int Opened, int Empty, int Failed)
{
    public static ScenarioLaunchResult None { get; } = new(0, 0, 0);

    public bool AnyOpened => Opened > 0;
}
