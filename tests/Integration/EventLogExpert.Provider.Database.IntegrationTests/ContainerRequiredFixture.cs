// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Database.IntegrationTests;

public sealed class ContainerRequiredFixture
{
    public ContainerRequiredFixture()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("EVENTLOG_CONTAINER")))
        {
            throw new InvalidOperationException(
                "Integration tests require EVENTLOG_CONTAINER to be set. " +
                "Set the variable and run: dotnet test tests/Integration/EventLogExpert.Provider.Database.IntegrationTests/ -p:RunSettingsFilePath=\"\"");
        }
    }
}
