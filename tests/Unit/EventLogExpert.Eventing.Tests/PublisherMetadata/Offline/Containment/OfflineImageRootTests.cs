// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline.Containment;

public sealed class OfflineImageRootTests
{
    [Fact]
    public void ContainsPath_DifferentCasing_IsTrue()
    {
        using OfflineTestImage image = OfflineTestImage.Create();

        string upperCased = Path.Combine(image.RootDirectory.ToUpperInvariant(), "Windows", "System32", "foo.dll");

        Assert.True(image.ImageRoot.ContainsPath(upperCased));
    }

    [Fact]
    public void ContainsPath_HostPathOutsideImageOnSameDrive_IsFalse()
    {
        // The scaffold lives on the host drive, so the host's own System32 is NOT under the image root - a
        // Path.GetPathRoot-based check (treating C:\ as the root) would wrongly accept it.
        using OfflineTestImage image = OfflineTestImage.Create();

        string hostPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ntdll.dll");

        Assert.False(image.ImageRoot.ContainsPath(hostPath));
    }

    [Fact]
    public void ContainsPath_PathUnderImageRoot_IsTrue()
    {
        using OfflineTestImage image = OfflineTestImage.Create();

        string inside = Path.Combine(image.ImageRoot.System32Directory, "foo.dll");

        Assert.True(image.ImageRoot.ContainsPath(inside));
    }

    [Fact]
    public void ContainsPath_SiblingDirectoryWithSharedPrefix_IsFalse()
    {
        using OfflineTestImage image = OfflineTestImage.Create();

        // Same string prefix as the image root but a different directory (…elx_img_ABC vs …elx_img_ABCevil).
        string sibling = image.RootDirectory + "evil" + Path.DirectorySeparatorChar + "foo.dll";

        Assert.False(image.ImageRoot.ContainsPath(sibling));
    }

    [Fact]
    public void TryCreate_GivenImageRootDirectory_ResolvesLayout()
    {
        using OfflineTestImage image = OfflineTestImage.Create();

        OfflineImageRoot? root = OfflineImageRoot.TryCreate(image.RootDirectory, logger: null);

        Assert.NotNull(root);
        Assert.Equal(image.RootDirectory, root!.ImageRoot);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows"), root.WindowsDirectory);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32"), root.System32Directory);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "config", "SOFTWARE"), root.SoftwareHivePath);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "config", "SYSTEM"), root.SystemHivePath);
    }

    [Fact]
    public void TryCreate_GivenWindowsDirectoryItself_ResolvesImageRootAsItsParent()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        string windowsDirectory = Path.Combine(image.RootDirectory, "Windows");

        OfflineImageRoot? root = OfflineImageRoot.TryCreate(windowsDirectory, logger: null);

        Assert.NotNull(root);
        Assert.Equal(image.RootDirectory, root!.ImageRoot);
        Assert.Equal(windowsDirectory, root.WindowsDirectory);
    }

    [Fact]
    public void TryCreate_ImageRootReachedViaJunction_FilesUnderItPassTheGuard()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        string rootLink = Path.Combine(Path.GetTempPath(), "elx_rootlink_" + Guid.NewGuid().ToString("N"));

        Assert.SkipUnless(OfflineTestImage.TryCreateJunction(rootLink, image.RootDirectory), "Could not create an NTFS junction for the reparse-point test.");

        try
        {
            // The image root is reached through a junction; TryCreate canonicalizes the boundary so a file under it is not
            // falsely rejected by the (reparse-resolving) guard - without that, every resolved file path would mismatch.
            OfflineImageRoot? viaJunction = OfflineImageRoot.TryCreate(rootLink, logger: null);

            Assert.NotNull(viaJunction);

            var guard = new OfflineRootGuard(viaJunction!, logger: null);

            guard.Assert(Path.Combine(viaJunction!.System32Directory, "foo.dll"), "resource");
        }
        finally
        {
            if (Directory.Exists(rootLink)) { Directory.Delete(rootLink); }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_NullOrEmpty_ReturnsNull(string? input)
    {
        Assert.Null(OfflineImageRoot.TryCreate(input!, logger: null));
    }

    [Fact]
    public void TryCreate_WhenPathHasInvalidCharacters_ReturnsNullWithoutThrowing()
    {
        // A NUL character makes Path.GetFullPath throw ArgumentException; the offline source promises a fail-closed
        // skip (logged null) for a hostile or malformed image path, never an exception bubbling out of the public
        // enumeration. The Assert.Null both asserts the contract and proves no exception was thrown.
        Assert.Null(OfflineImageRoot.TryCreate("foo\0bar", logger: null));
    }

    [Fact]
    public void TryCreate_WhenSoftwareHiveMissing_ReturnsNull()
    {
        string root = CreateConfigScaffold(out string configDirectory);

        try
        {
            File.WriteAllBytes(Path.Combine(configDirectory, "SYSTEM"), []);
            // SOFTWARE intentionally absent.

            Assert.Null(OfflineImageRoot.TryCreate(root, logger: null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryCreate_WhenSystemHiveMissing_ReturnsNull()
    {
        string root = CreateConfigScaffold(out string configDirectory);

        try
        {
            File.WriteAllBytes(Path.Combine(configDirectory, "SOFTWARE"), []);
            // SYSTEM intentionally absent.

            Assert.Null(OfflineImageRoot.TryCreate(root, logger: null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateConfigScaffold(out string configDirectory)
    {
        string root = Path.Combine(Path.GetTempPath(), "elx_imgroot_" + Guid.NewGuid().ToString("N"));
        configDirectory = Path.Combine(root, "Windows", "System32", "config");
        Directory.CreateDirectory(configDirectory);

        return root;
    }
}
