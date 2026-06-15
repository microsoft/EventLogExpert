// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.Runtime.Tests.Scenarios;

internal static class ScenarioTestData
{
    public static ScenarioDefinition Combined(string id, params string[] channels) => new()
    {
        Id = id,
        Name = id,
        Purpose = "p",
        Group = ScenarioGroup.SystemHealth,
        Channels = [.. channels],
        RequiresAdmin = channels.Any(IsAdminChannel),
        Filters = [.. channels.Select(channel => LogNameRow(channel, 1000))]
    };

    public static BuiltInScenarioRegistry Registry(params ScenarioDefinition[] scenarios) =>
        new([new FakeSource(scenarios)]);

    public static ScenarioDefinition Single(string id, string channel, int eventId) => new()
    {
        Id = id,
        Name = id,
        Purpose = "p",
        Group = ScenarioGroup.SystemHealth,
        Channels = [channel],
        RequiresAdmin = IsAdminChannel(channel),
        Filters = [IdRow(eventId)]
    };

    private static ScenarioFilterRow IdRow(int eventId) =>
        new(new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = eventId.ToString()
            },
            []));

    private static bool IsAdminChannel(string channel) =>
        channel is "Security" or "State";

    private static ScenarioFilterRow LogNameRow(string channel, int eventId) =>
        new(new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.LogName,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = channel
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Id,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = eventId.ToString()
                    },
                    JoinWithAny: false)
            ]));

    private sealed class FakeSource(params ScenarioDefinition[] scenarios) : IScenarioSource
    {
        public IReadOnlyList<ScenarioDefinition> GetScenarios() => scenarios;
    }
}
