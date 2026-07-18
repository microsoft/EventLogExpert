// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>The terminal state of <see cref="IScenarioLaunchService.LaunchFromFolderAsync" />.</summary>
public enum ScenarioFolderOutcome
{
    Cancelled,
    Error,
    NoMatchingLogs,
    NoLogsOpened,
    Completed
}

/// <summary>The result of launching a scenario against exported logs in a folder.</summary>
public sealed record ScenarioFolderLaunchResult
{
    public required ScenarioFolderOutcome Outcome { get; init; }

    public int Matched { get; init; }

    public int Unreadable { get; init; }

    public int Opened { get; init; }

    public int Empty { get; init; }

    public int Failed { get; init; }

    public ImmutableArray<string> MatchedChannels { get; init; } = [];

    public ImmutableArray<string> MissingChannels { get; init; } = [];

    public string? Message { get; init; }

    public static ScenarioFolderLaunchResult Cancelled { get; } = new() { Outcome = ScenarioFolderOutcome.Cancelled };

    public static ScenarioFolderLaunchResult Error(string message, int unreadable = 0) =>
        new() { Outcome = ScenarioFolderOutcome.Error, Message = message, Unreadable = unreadable };
}
