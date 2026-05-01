// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;

namespace EventLogExpert.Eventing.Tests.Helpers;

public sealed class LogNamesTests
{
    [Fact]
    public void AdminOnlyLiveLogNames_ContainsExpectedNames()
    {
        Assert.Contains(LogNames.SecurityLog, LogNames.AdminOnlyLiveLogNames);
        Assert.Contains(LogNames.StateLog, LogNames.AdminOnlyLiveLogNames);
        Assert.Equal(2, LogNames.AdminOnlyLiveLogNames.Count);
    }

    [Theory]
    [InlineData("security")]
    [InlineData("SECURITY")]
    [InlineData("state")]
    [InlineData("STATE")]
    public void AdminOnlyLiveLogNames_ShouldMatchCaseInsensitively(string input)
    {
        Assert.Contains(input, LogNames.AdminOnlyLiveLogNames);
    }

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal("Application", LogNames.ApplicationLog);
        Assert.Equal("Security", LogNames.SecurityLog);
        Assert.Equal("State", LogNames.StateLog);
        Assert.Equal("System", LogNames.SystemLog);
    }
}
