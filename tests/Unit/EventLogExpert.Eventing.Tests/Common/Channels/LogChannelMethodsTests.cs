// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;

namespace EventLogExpert.Eventing.Tests.Common.Channels;

public sealed class LogChannelMethodsTests
{
    [Fact]
    public void GetMenuPath_WhenChannelHasEmptySegments_ShouldIgnoreEmpties()
    {
        var path = LogChannelMethods.GetMenuPath("Provider//Operational");

        Assert.Equal(["Provider", "Operational"], path);
    }

    [Fact]
    public void GetMenuPath_WhenChannelHasNestedSlashes_ShouldExpandEachSubChannel()
    {
        var path = LogChannelMethods.GetMenuPath("Microsoft-Windows-Foo/Bar/Baz");

        Assert.Equal(["Microsoft", "Windows", "Foo", "Bar", "Baz"], path);
    }

    [Fact]
    public void GetMenuPath_WhenInputIsNullOrWhitespace_ShouldReturnEmpty()
    {
        Assert.Empty(LogChannelMethods.GetMenuPath(string.Empty));
        Assert.Empty(LogChannelMethods.GetMenuPath("   "));
    }

    [Fact]
    public void GetMenuPath_WhenLogNameContainsSpaces_ShouldKeepSegmentIntact()
    {
        var path = LogChannelMethods.GetMenuPath("Windows PowerShell");

        Assert.Equal(["Windows PowerShell"], path);
    }

    [Fact]
    public void GetMenuPath_WhenMicrosoftWindowsChannelHasHyphen_ShouldKeepChannelIntact()
    {
        var path = LogChannelMethods.GetMenuPath("Microsoft-Windows-Kernel-Power/Thermal-Operational");

        Assert.Equal(["Microsoft", "Windows", "Kernel-Power", "Thermal-Operational"], path);
    }

    [Fact]
    public void GetMenuPath_WhenMicrosoftWindowsChannel_ShouldExpandToFourSegments()
    {
        var path = LogChannelMethods.GetMenuPath("Microsoft-Windows-AAD/Operational");

        Assert.Equal(["Microsoft", "Windows", "AAD", "Operational"], path);
    }

    [Fact]
    public void GetMenuPath_WhenMicrosoftWindowsHasNoRemainder_ShouldFallBackToProviderAsLeaf()
    {
        // Defensive: a malformed "Microsoft-Windows-/Operational" should not produce an empty
        // folder label. Falls through to the non-prefix branch and keeps the literal provider.
        var path = LogChannelMethods.GetMenuPath("Microsoft-Windows-/Operational");

        Assert.Equal(["Microsoft-Windows-", "Operational"], path);
    }

    [Fact]
    public void GetMenuPath_WhenMicrosoftWindowsProviderHasHyphen_ShouldKeepProviderIntact()
    {
        var path = LogChannelMethods.GetMenuPath("Microsoft-Windows-Kernel-Power/Operational");

        Assert.Equal(["Microsoft", "Windows", "Kernel-Power", "Operational"], path);
    }

    [Fact]
    public void GetMenuPath_WhenMicrosoftWindowsProviderHasMultipleHyphens_ShouldKeepProviderIntact()
    {
        var path = LogChannelMethods.GetMenuPath("Microsoft-Windows-AppXDeployment-Server/Operational");

        Assert.Equal(["Microsoft", "Windows", "AppXDeployment-Server", "Operational"], path);
    }

    [Fact]
    public void GetMenuPath_WhenNonMicrosoftProviderHasHyphens_ShouldNotSplitOnHyphen()
    {
        var path = LogChannelMethods.GetMenuPath("Microsoft-Client-Licensing-Platform/Admin");

        Assert.Equal(["Microsoft-Client-Licensing-Platform", "Admin"], path);
    }

    [Fact]
    public void GetMenuPath_WhenPrefixCasingDiffers_ShouldStillRecognizeMicrosoftWindows()
    {
        var path = LogChannelMethods.GetMenuPath("microsoft-windows-AAD/Operational");

        Assert.Equal(["Microsoft", "Windows", "AAD", "Operational"], path);
    }

    [Fact]
    public void GetMenuPath_WhenProviderEndsWithSlash_ShouldOmitTrailingEmptyChannel()
    {
        var path = LogChannelMethods.GetMenuPath("Provider/");

        Assert.Equal(["Provider"], path);
    }

    [Fact]
    public void GetMenuPath_WhenSimpleProviderWithChannel_ShouldSplitOnSlashOnly()
    {
        var path = LogChannelMethods.GetMenuPath("OpenSSH/Operational");

        Assert.Equal(["OpenSSH", "Operational"], path);
    }

    [Fact]
    public void GetMenuPath_WhenSimpleRootLog_ShouldReturnSingleSegment()
    {
        var path = LogChannelMethods.GetMenuPath("Application");

        Assert.Equal(["Application"], path);
    }

    [Fact]
    public void HardCodedLiveChannels_ContainsExpectedNames()
    {
        Assert.Contains("Application", LogChannelMethods.HardCodedLiveChannels);
        Assert.Contains("System", LogChannelMethods.HardCodedLiveChannels);
        Assert.Contains("Security", LogChannelMethods.HardCodedLiveChannels);
        Assert.Equal(3, LogChannelMethods.HardCodedLiveChannels.Count);
    }

    [Theory]
    [InlineData("application")]
    [InlineData("APPLICATION")]
    [InlineData("Application")]
    public void HardCodedLiveChannels_ShouldMatchCaseInsensitively(string input)
    {
        Assert.Contains(input, LogChannelMethods.HardCodedLiveChannels);
    }
}
