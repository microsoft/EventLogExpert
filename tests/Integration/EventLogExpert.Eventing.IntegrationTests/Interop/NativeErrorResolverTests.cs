// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;

namespace EventLogExpert.Eventing.IntegrationTests.Interop;

public sealed class NativeErrorResolverTests
{
    [Fact]
    public void GetErrorMessage_ShouldReturnConsistentResults()
    {
        // Arrange - use a well-known HRESULT (ERROR_SUCCESS = 0)
        const uint ErrorSuccess = 0;

        // Act
        var result1 = NativeErrorResolver.GetErrorMessage(ErrorSuccess);
        var result2 = NativeErrorResolver.GetErrorMessage(ErrorSuccess);

        // Assert - same string returned (cached)
        Assert.NotNull(result1);
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void GetErrorMessage_WhenUnknownCode_ShouldReturnHexFallback()
    {
        // Arrange - use a code unlikely to have a system message
        const uint UnknownCode = 0xDEADBEEF;

        // Act
        var result = NativeErrorResolver.GetErrorMessage(UnknownCode);

        // Assert - if the OS doesn't resolve this code, we get the hex fallback.
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
        // Arrange - STATUS_SUCCESS = 0
        const uint StatusSuccess = 0;

        // Act
        var result1 = NativeErrorResolver.GetNtStatusMessage(StatusSuccess);
        var result2 = NativeErrorResolver.GetNtStatusMessage(StatusSuccess);

        // Assert
        Assert.NotNull(result1);
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void GetSystemMessageCached_KnownCode_ShouldReturnNonEmptyAndCache()
    {
        // Arrange - ERROR_FILE_NOT_FOUND (2) has a system message on all Windows locales.
        const uint ErrorFileNotFound = 2;

        // Act
        var result1 = NativeErrorResolver.GetSystemMessageCached(ErrorFileNotFound);
        var result2 = NativeErrorResolver.GetSystemMessageCached(ErrorFileNotFound);

        // Assert - resolves to a message and the cached second call returns the same instance.
        Assert.NotEqual(string.Empty, result1);
        Assert.Same(result1, result2);
    }

    [Fact]
    public void GetSystemMessageCached_RepeatedUnknownCode_ShouldReturnConsistentCachedMiss()
    {
        // Arrange - repeated lookups of an unresolved code must be served from cache (no per-call P/Invoke).
        const uint UnknownCode = 0xDEADBEE0;

        // Act
        var result1 = NativeErrorResolver.GetSystemMessageCached(UnknownCode);

        // Assert - the first lookup caches its result (hit or miss), so repeated events skip the Win32 P/Invoke.
        Assert.True(NativeErrorResolver.IsSystemMessageCodeCached(UnknownCode));

        var result2 = NativeErrorResolver.GetSystemMessageCached(UnknownCode);

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void GetSystemMessageCached_UnknownCode_ShouldReturnEmptyPreservingUnsubstitutedContract()
    {
        // Arrange - a code with no system message; FormatSystemMessage returns null there.
        const uint UnknownCode = 0xDEADBEEF;

        // Act
        var result = NativeErrorResolver.GetSystemMessageCached(UnknownCode);

        // Assert - a miss yields string.Empty (so the %%N token is left unsubstituted), unless this
        // OS/locale happens to resolve the code, in which case a real message is acceptable.
        if (NativeMethods.FormatSystemMessage(UnknownCode) is null)
        {
            Assert.Equal(string.Empty, result);
        }
        else
        {
            Assert.NotNull(result);
        }
    }

    [Fact]
    public void MaxCacheSize_ShouldBeReasonableBound()
    {
        // Assert - the cache limit is the expected constant
        Assert.Equal(4096, NativeErrorResolver.MaxCacheSize);
    }
}
