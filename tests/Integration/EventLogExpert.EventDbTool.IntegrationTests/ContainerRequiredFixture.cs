// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.EventDbTool.IntegrationTests;

public sealed class ContainerRequiredFixture
{
    public ContainerRequiredFixture()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EVENTLOG_CONTAINER")))
        {
            throw new InvalidOperationException(
                "Integration tests must run in a container. " +
                "Use './scripts/run-integration-tests.ps1' or set EVENTLOG_CONTAINER=1 for explicit local execution.");
        }
    }
}
