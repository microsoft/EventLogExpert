// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;

namespace EventLogExpert.Eventing.Tests.Interop;

public sealed class NativeErrorResolverTests
{
    [Fact]
    public void GetErrorMessage_ShouldReturnConsistentResults()
    {
        // Arrange — use a well-known HRESULT (ERROR_SUCCESS = 0)
        const uint ErrorSuccess = 0;

        // Act
        var result1 = NativeErrorResolver.GetErrorMessage(ErrorSuccess);
        var result2 = NativeErrorResolver.GetErrorMessage(ErrorSuccess);

        // Assert — same string returned (cached)
        Assert.NotNull(result1);
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void GetErrorMessage_WhenUnknownCode_ShouldReturnHexFallback()
    {
        // Arrange — use a code unlikely to have a system message
        const uint UnknownCode = 0xDEADBEEF;

        // Act
        var result = NativeErrorResolver.GetErrorMessage(UnknownCode);

        // Assert — if the OS doesn't resolve this code, we get the hex fallback.
        // On some OS versions/locales FormatMessage may resolve it, so only assert
        // the hex representation when the system has no message for this code.
        var systemMessage = NativeMethods.FormatSystemMessage(UnknownCode);
        var ntStatusMessage = NativeMethods.FormatNtStatusMessage(UnknownCode);

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
        const uint StatusSuccess = 0;

        // Act
        var result1 = NativeErrorResolver.GetNtStatusMessage(StatusSuccess);
        var result2 = NativeErrorResolver.GetNtStatusMessage(StatusSuccess);

        // Assert
        Assert.NotNull(result1);
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void MaxCacheSize_ShouldBeReasonableBound()
    {
        // Assert — the cache limit is the expected constant
        Assert.Equal(4096, NativeErrorResolver.MaxCacheSize);
    }
}
