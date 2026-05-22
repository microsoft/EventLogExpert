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
                "Integration tests require EVENTLOG_CONTAINER to be set. " +
                "Set the variable and run: dotnet test tests/Integration/EventLogExpert.ProviderDatabase.IntegrationTests/ -p:RunSettingsFilePath=\"\"");
        }
    }
}
