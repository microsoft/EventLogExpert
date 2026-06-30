// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.CreateDatabase;

namespace EventLogExpert.DatabaseTools.Tests.CreateDatabase;

public sealed class CreateDatabaseSkipProvidersMessageTests
{
    [Theory]
    [InlineData(0, "Found 0 providers in skip.txt. These will not be included in the new database.")]
    [InlineData(1, "Found 1 provider in skip.txt. It will not be included in the new database.")]
    [InlineData(2, "Found 2 providers in skip.txt. These will not be included in the new database.")]
    public void FormatSkippedProvidersMessage_FormatsProviderCountGrammar(int providerCount, string expected)
    {
        string message = CreateDatabaseOperation.FormatSkippedProvidersMessage(providerCount, "skip.txt");

        Assert.Equal(expected, message);
    }
}
