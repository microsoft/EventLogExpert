// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class ResolutionStatusTokensTests
{
    [Fact]
    public void Classify_ForEmptyToken_ReturnsResolved() =>
        Assert.Equal(EventResolutionStatus.Resolved, ResolutionStatusTokens.Classify(string.Empty));

    [Theory]
    [InlineData(EventResolutionStatus.Resolved)]
    [InlineData(EventResolutionStatus.NoProvider)]
    [InlineData(EventResolutionStatus.NoMessage)]
    [InlineData(EventResolutionStatus.Failed)]
    public void Classify_RoundTripsEveryFormattedToken(EventResolutionStatus status) =>
        Assert.Equal(status, ResolutionStatusTokens.Classify(ResolutionStatusTokens.Format(status)));

    [Theory]
    [InlineData(EventResolutionStatus.Resolved, ResolutionStatusTokens.Resolved)]
    [InlineData(EventResolutionStatus.NoProvider, ResolutionStatusTokens.NoProvider)]
    [InlineData(EventResolutionStatus.NoMessage, ResolutionStatusTokens.NoMessage)]
    [InlineData(EventResolutionStatus.Failed, ResolutionStatusTokens.Failed)]
    public void Format_ForEachStatus_ReturnsItsFrozenToken(EventResolutionStatus status, string expectedToken) =>
        Assert.Equal(expectedToken, ResolutionStatusTokens.Format(status));
}
