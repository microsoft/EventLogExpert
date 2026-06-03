// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.ElevationHelper.IntegrationTests;

public sealed class ContainerRequiredFixture
{
    public ContainerRequiredFixture()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EVENTLOG_CONTAINER")))
        {
            throw new InvalidOperationException(
                "Integration tests require EVENTLOG_CONTAINER to be set. " +
                "Use scripts/run-tests.ps1 (recommended — runs in a container) or set the variable manually for host execution.");
        }
    }
}
