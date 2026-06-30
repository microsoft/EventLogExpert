// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline.Containment;

public sealed class OfflineImagePathMapperTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32\foo.dll")]
    [InlineData(@"%SystemRoot%\System32\foo.dll")]
    [InlineData(@"%SystemDrive%\App\bar.dll")]
    [InlineData("baz.dll")]
    [InlineData(@"\Windows\notepad.exe")]
    public void ReRoot_AnyAcceptedValue_NeverYieldsABareLeaf(string registryPath)
    {
        // Mapped paths must keep directory information so host fallback paths stay unreachable.
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        string? result = mapper.Map(registryPath);

        Assert.NotNull(result);
        Assert.NotEqual(Path.GetFileName(result), result);
        Assert.StartsWith(image.RootDirectory, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReRoot_BareLeaf_RedirectsUnderImageSystem32()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        string? result = mapper.Map("APHostRes.dll");

        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "APHostRes.dll"), result, ignoreCase: true);
    }

    [Fact]
    public void ReRoot_DotDotTraversalThatEscapesImageRoot_IsDropped()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        // The traversal must escape any scaffold depth so the mapper proves fail-closed dropping.
        string escaping = @"C:\Windows\" + string.Concat(Enumerable.Repeat(@"..\", 40)) + "escape.dll";

        Assert.Null(mapper.Map(escaping));
    }

    [Fact]
    public void ReRoot_DotDotTraversalThatStaysWithinImageRoot_IsKept()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        string? result = mapper.Map(@"C:\Windows\System32\..\drivers\foo.dll");

        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "drivers", "foo.dll"), result, ignoreCase: true);
    }

    [Fact]
    public void ReRoot_DriveAbsolutePath_RootsUnderImageNotHost()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        string? result = mapper.Map(@"C:\Windows\System32\foo.dll");

        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "foo.dll"), result, ignoreCase: true);
    }

    [Fact]
    public void ReRoot_DriveAbsoluteWithNonCDriveAndUpperCase_StillRootsUnderImage()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        string? result = mapper.Map(@"D:\WINDOWS\System32\bar.dll");

        Assert.Equal(Path.Combine(image.RootDirectory, "WINDOWS", "System32", "bar.dll"), result, ignoreCase: true);
    }

    [Fact]
    public void ReRoot_PathWithEmbeddedNullCharacter_IsDroppedWithoutThrowing()
    {
        // Embedded NULs must be dropped fail-closed before Path.GetFullPath can throw.
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        Assert.Null(mapper.Map("C:\\Windows\\System32\\foo\0bar.dll"));
    }

    [Theory]
    [InlineData(@"%ProgramFiles%\Windows Defender\MpEvMsg.dll", @"Program Files\Windows Defender\MpEvMsg.dll")]
    [InlineData(@"%programfiles%\Windows Defender\MpClient.dll", @"Program Files\Windows Defender\MpClient.dll")]
    [InlineData(@"%ProgramFiles(x86)%\Vendor\app.dll", @"Program Files (x86)\Vendor\app.dll")]
    [InlineData(@"%CommonProgramFiles%\Microsoft Shared\Ink\mip.exe", @"Program Files\Common Files\Microsoft Shared\Ink\mip.exe")]
    [InlineData(@"%CommonProgramFiles(x86)%\Microsoft Shared\Ink\mraut.dll", @"Program Files (x86)\Common Files\Microsoft Shared\Ink\mraut.dll")]
    [InlineData(@"%ProgramData%\Microsoft\Windows Defender\Default\MpEngine.dll", @"ProgramData\Microsoft\Windows Defender\Default\MpEngine.dll")]
    public void ReRoot_ProgramDirectoryTokens_MapToImageRelativeLocation(string registryPath, string expectedRelative)
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        string? result = mapper.Map(registryPath);

        Assert.Equal(Path.Combine(image.RootDirectory, expectedRelative), result, ignoreCase: true);
    }

    [Fact]
    public void ReRoot_QuotedAndPaddedValue_IsTrimmedThenReRooted()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        string? result = mapper.Map("  \"C:\\Windows\\System32\\foo.dll\"  ");

        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "foo.dll"), result, ignoreCase: true);
    }

    [Fact]
    public void ReRoot_RootedNoDrivePath_RootsUnderImage()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        string? result = mapper.Map(@"\Windows\System32\foo.dll");

        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "foo.dll"), result, ignoreCase: true);
    }

    [Fact]
    public void ReRoot_SystemDriveToken_MapsToImageRoot()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        string? result = mapper.Map(@"%SystemDrive%\Program Files\App\bar.dll");

        Assert.Equal(Path.Combine(image.RootDirectory, "Program Files", "App", "bar.dll"), result, ignoreCase: true);
    }

    [Theory]
    [InlineData(@"%SystemRoot%\System32\foo.dll")]
    [InlineData(@"%systemroot%\System32\foo.dll")]
    [InlineData(@"%windir%\System32\foo.dll")]
    public void ReRoot_SystemRootTokens_MapToImageWindows(string registryPath)
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        string? result = mapper.Map(registryPath);

        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "foo.dll"), result, ignoreCase: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:foo.dll")]
    [InlineData(@"\\server\share\foo.dll")]
    [InlineData(@"\\?\C:\Windows\System32\foo.dll")]
    [InlineData(@"\\.\C:\foo.dll")]
    [InlineData(@"\??\C:\Windows\foo.dll")]
    [InlineData(@"%APPDATA%\Vendor\foo.dll")]
    [InlineData(@"%ProgramFiles%evil\foo.dll")]
    [InlineData(@"%SystemRoot%System32\foo.dll")]
    [InlineData(@"%SystemRoot%\%Nested%\foo.dll")]
    [InlineData("foo.dll:stream")]
    public void ReRoot_UnsafeOrUnsupportedForms_AreDroppedFailClosed(string? registryPath)
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var mapper = new OfflineImagePathMapper(image.ImageRoot, logger: null);

        Assert.Null(mapper.Map(registryPath));
    }
}
