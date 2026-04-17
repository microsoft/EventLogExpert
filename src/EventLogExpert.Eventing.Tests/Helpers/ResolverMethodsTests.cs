// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;

namespace EventLogExpert.Eventing.Tests.Helpers;

public sealed class ResolverMethodsTests
{
    [Fact]
    public void GetErrorMessage_ShouldReturnConsistentResults()
    {
        // Arrange — use a well-known HRESULT (ERROR_SUCCESS = 0)
        const uint errorSuccess = 0;

        // Act
        var result1 = ResolverMethods.GetErrorMessage(errorSuccess);
        var result2 = ResolverMethods.GetErrorMessage(errorSuccess);

        // Assert — same string returned (cached)
        Assert.NotNull(result1);
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void GetErrorMessage_WhenUnknownCode_ShouldReturnHexFallback()
    {
        // Arrange — use a code unlikely to have a system message
        const uint unknownCode = 0xDEADBEEF;

        // Act
        var result = ResolverMethods.GetErrorMessage(unknownCode);

        // Assert — if the OS doesn't resolve this code, we get the hex fallback.
        // On some OS versions/locales FormatMessage may resolve it, so only assert
        // the hex representation when the system has no message for this code.
        var systemMessage = NativeMethods.FormatSystemMessage(unknownCode);
        var ntStatusMessage = NativeMethods.FormatNtStatusMessage(unknownCode);

        if (systemMessage is null && ntStatusMessage is null)
        {
            Assert.Equal("0xDEADBEEF", result);
        }
        else
        {
            Assert.NotNull(result);
            Assert.NotEqual(string.Empty, result);
        }
    }

    [Fact]
    public void GetNtStatusMessage_ShouldReturnConsistentResults()
    {
        // Arrange — STATUS_SUCCESS = 0
        const uint statusSuccess = 0;

        // Act
        var result1 = ResolverMethods.GetNtStatusMessage(statusSuccess);
        var result2 = ResolverMethods.GetNtStatusMessage(statusSuccess);

        // Assert
        Assert.NotNull(result1);
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void MaxCacheSize_ShouldBeReasonableBound()
    {
        // Assert — the cache limit is the expected constant
        Assert.Equal(4096, ResolverMethods.MaxCacheSize);
    }
}
