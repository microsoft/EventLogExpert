// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.ProviderDatabase.IntegrationTests;

public sealed class ContainerRequiredFixture
{
    public ContainerRequiredFixture()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("EVENTLOG_CONTAINER")))
        {
            throw new InvalidOperationException(
                "Integration tests require the EVENTLOG_CONTAINER env var. " +
                "Run via scripts/run-integration-tests.ps1 or set the var manually.");
        }
    }
}
