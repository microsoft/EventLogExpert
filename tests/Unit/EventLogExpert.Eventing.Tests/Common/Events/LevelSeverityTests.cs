// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class LevelSeverityTests
{
    [Theory]
    [InlineData("Critical", SeverityLevel.Critical)]
    [InlineData("Error", SeverityLevel.Error)]
    [InlineData("Warning", SeverityLevel.Warning)]
    [InlineData("Information", SeverityLevel.Information)]
    [InlineData("Verbose", SeverityLevel.Verbose)]
    public void FromLevelName_MapsInvariantEnumNames(string level, SeverityLevel expected) =>
        Assert.Equal(expected, LevelSeverity.FromLevelName(level));

    [Theory]
    [InlineData("error")]
    [InlineData("WARNING")]
    [InlineData("")]
    [InlineData("Audit Success")]
    [InlineData(null)]
    public void FromLevelName_ReturnsNullForUnrecognizedOrMiscasedNames(string? level) =>
        Assert.Null(LevelSeverity.FromLevelName(level));
}
