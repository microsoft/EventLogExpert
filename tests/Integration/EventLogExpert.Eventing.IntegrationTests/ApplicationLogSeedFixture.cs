// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics;

namespace EventLogExpert.Eventing.IntegrationTests;

public sealed class ApplicationLogSeedFixture : IAsyncLifetime
{
    private const int SeedCount = 10;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EVENTLOG_CONTAINER")))
        {
            return ValueTask.CompletedTask;
        }

        using var log = new EventLog("Application");
        log.Source = "Application";

        for (var i = 1; i <= SeedCount; i++)
        {
            log.WriteEntry($"Integration test warmup {i}", EventLogEntryType.Information);
        }

        return ValueTask.CompletedTask;
    }
}
